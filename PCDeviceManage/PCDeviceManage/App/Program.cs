using log4net;
using log4net.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PCDeviceManage.Devices.Audio;
using PCDeviceManage.Devices.Display;
using PCDeviceManage.Devices.Power;
using PCDeviceManage.Tunnel;
using PCDeviceManage.Web;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PCDeviceManage.App
{
    static class Program
    {
        private const string MutexName = @"Local\PCManage.PCDeviceManage";
        // 旧版本在启动文件夹中创建的快捷方式名（Application.ProductName + ".lnk"）。
        private const string LegacyStartupShortcut = "PCDeviceManage.lnk";

        private static ILog _log;

        [STAThread]
        static void Main()
        {
            log4net.GlobalContext.Properties["pid"] = Environment.ProcessId;
            XmlConfigurator.ConfigureAndWatch(LogManager.GetRepository(Assembly.GetEntryAssembly()),
                new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config")));
            _log = LogManager.GetLogger(typeof(Program));

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("PC控制已经在运行，可以在任务栏通知区域找到它的图标。", "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ApplicationConfiguration.Initialize();

                AppSettings settings;
                bool tokenCreated;
                try
                {
                    settings = AppSettings.LoadOrCreate(AppSettings.DefaultPath, AppSettings.DefaultTokenPath, AppSettings.LegacyDataDirectory, out tokenCreated);
                    if (tokenCreated)
                    {
                        _log.Warn("已生成访问令牌并保存到 " + AppSettings.DefaultTokenPath + "，打开控制台时在登录页输入该令牌。");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("读取配置失败", ex);
                    MessageBox.Show("读取配置失败：" + ex.Message, "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    if (AppSettings.DeleteLegacyDirectory(AppSettings.LegacyDataDirectory))
                    {
                        _log.Info("已将配置迁移到程序目录，并删除旧目录 " + AppSettings.LegacyDataDirectory);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("删除旧数据目录失败：" + AppSettings.LegacyDataDirectory, ex);
                }

                Autostart autostart = Autostart.ForCurrentUser(Application.ExecutablePath);
                try
                {
                    if (autostart.MigrateStartupShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyStartupShortcut))
                    {
                        _log.Info("已将启动文件夹中的快捷方式迁移为注册表开机自启。");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("迁移旧的开机自启快捷方式失败", ex);
                }

                WebApplication app;
                try
                {
                    app = AppHost.Build(settings.WebHost, settings.WebPort, services => ConfigureServices(services, settings));
                }
                catch (Exception ex)
                {
                    _log.Error("初始化失败", ex);
                    MessageBox.Show("初始化失败：" + ex.Message, "PC控制", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    // 托管服务（显示器扫描、frp）先于 Kestrel 启动；端口被占用时 Web 控制台不可用，但托盘和 frp 照常运行。
                    bool webStarted = true;
                    try
                    {
                        app.StartAsync().GetAwaiter().GetResult();
                        _log.Info("Web 控制台已启动：" + TrayApplicationContext.ConsoleUrl(settings));
                    }
                    catch (Exception ex)
                    {
                        webStarted = false;
                        _log.Error($"Web 控制台启动失败，端口 {settings.WebPort} 可能已被占用，可以在 {AppSettings.DefaultPath} 中修改 webPort。", ex);
                    }

                    using (var tray = new TrayApplicationContext(
                        settings,
                        app.Services.GetRequiredService<ShutdownScheduler>(),
                        app.Services.GetRequiredService<FrpTunnel>(),
                        autostart,
                        tokenCreated,
                        webStarted))
                    {
                        Application.Run(tray);
                    }
                }
                finally
                {
                    try
                    {
                        app.StopAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _log.Error("停止服务出错", ex);
                    }
                    app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }

        private static void ConfigureServices(IServiceCollection services, AppSettings settings)
        {
            services.AddSingleton(settings);
            services.AddSingleton(new TokenAuthenticator(settings.AccessToken));
            services.AddSingleton<PowerService>();
            services.AddSingleton<AudioService>();
            services.AddSingleton(provider => new ShutdownScheduler(provider.GetRequiredService<PowerService>().Shutdown));
            services.AddSingleton(provider => new MonitorRegistry(new SystemMonitorEnumerator(), _log));
            services.AddSingleton(provider => new FrpTunnel(Path.Combine(AppContext.BaseDirectory, "frp"), settings.FrpEnabled, _log));

            services.AddLifetimeHook(provider => provider.GetRequiredService<MonitorRegistry>().Start());
            services.AddLifetimeHook(
                provider => provider.GetRequiredService<FrpTunnel>().Start(),
                provider => provider.GetRequiredService<FrpTunnel>().Stop());
        }
    }
}
