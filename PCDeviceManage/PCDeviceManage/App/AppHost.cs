using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCDeviceManage
{
    /// <summary>
    /// 应用主机：一个 WebApplication 同时承载 Web 控制台和后台服务，统一启动和停止；
    /// 通过工厂注册的单例在主机释放时由容器 Dispose。
    /// </summary>
    public static class AppHost
    {
        public static WebApplication Build(string webHost, int webPort, Action<IServiceCollection> configureServices)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            // 应用日志走 log4net，不启用 ASP.NET Core 自己的日志输出。
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 64 * 1024;
                options.Listen(WebConsole.ResolveAddress(webHost), webPort);
            });
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
            configureServices(builder.Services);

            WebApplication app = builder.Build();
            WebConsole.Configure(app);
            return app;
        }

        /// <summary>注册一个随主机启动、停止时执行指定动作的托管服务。</summary>
        public static IServiceCollection AddLifetimeHook(this IServiceCollection services, Action<IServiceProvider> onStart, Action<IServiceProvider> onStop = null)
        {
            return services.AddSingleton<IHostedService>(provider => new LifetimeHook(
                () => onStart(provider),
                onStop == null ? (Action)null : () => onStop(provider)));
        }

        private sealed class LifetimeHook : IHostedService
        {
            private readonly Action _onStart;
            private readonly Action _onStop;

            public LifetimeHook(Action onStart, Action onStop)
            {
                _onStart = onStart;
                _onStop = onStop;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _onStart();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _onStop?.Invoke();
                return Task.CompletedTask;
            }
        }
    }
}
