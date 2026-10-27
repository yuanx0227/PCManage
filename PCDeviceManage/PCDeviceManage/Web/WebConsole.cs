using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace PCDeviceManage
{
    /// <summary>
    /// 本机 Web 控制台的中间件和端点（Minimal API）。只负责路由、鉴权、防 CSRF 和 JSON 序列化，
    /// 具体操作交给容器中的各个服务。接口统一返回 { "ok": true, "data": ... } 或 { "ok": false, "error": "..." }。
    /// </summary>
    public sealed class WebConsole
    {
        /// <summary>标记本服务映射的端点；路由系统生成的 405 等端点没有它，不做鉴权检查。</summary>
        private sealed class ApiEndpoint
        {
            public bool IsPublic { get; set; }
        }

        private sealed class ApiException : Exception
        {
            public ApiException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }

            public int StatusCode { get; }
        }

        private static readonly ILog _log = LogManager.GetLogger(typeof(WebConsole));

        private readonly MonitorRegistry _monitorRegistry;
        private readonly AudioService _audioService;
        private readonly PowerService _powerService;
        private readonly ShutdownScheduler _shutdownScheduler;
        private readonly TokenAuthenticator _authenticator;

        private WebConsole(IServiceProvider services)
        {
            _monitorRegistry = services.GetRequiredService<MonitorRegistry>();
            _audioService = services.GetRequiredService<AudioService>();
            _powerService = services.GetRequiredService<PowerService>();
            _shutdownScheduler = services.GetRequiredService<ShutdownScheduler>();
            _authenticator = services.GetRequiredService<TokenAuthenticator>();
        }

        /// <summary>给主机加上控制台的中间件和端点。</summary>
        public static void Configure(WebApplication app)
        {
            var console = new WebConsole(app.Services);
            app.Use(console.HandleErrors);
            app.UseStatusCodePages(WriteStatusCodePage);
            app.UseRouting();
            app.Use(console.Guard);
            console.MapEndpoints(app);
        }

        /// <summary>监听地址：+、* 或 0.0.0.0 表示所有地址（IPv4 和 IPv6），localhost 表示回环地址。</summary>
        public static IPAddress ResolveAddress(string host)
        {
            if (host == "+" || host == "*" || host == "0.0.0.0")
            {
                return IPAddress.IPv6Any;
            }
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }

            IPAddress address;
            if (IPAddress.TryParse(host, out address))
            {
                return address;
            }
            throw new ArgumentException("webHost 必须是 IP 地址、localhost 或 +：" + host);
        }

        private void MapEndpoints(WebApplication app)
        {
            Map(app.MapGet("/favicon.ico", () => Resource("PCDeviceManage.app.ico", "image/x-icon")), true);
            Map(app.MapGet("/login", () => Resource("PCDeviceManage.wwwroot.login.html", "text/html; charset=utf-8")), true);
            foreach (string page in new[] { "/", "/home", "/home/index" })
            {
                Map(app.MapGet(page, () => Resource("PCDeviceManage.wwwroot.index.html", "text/html; charset=utf-8")));
            }
            // 显式转换成 Delegate：直接传方法组会匹配 RequestDelegate 重载，返回的 IResult 会被丢弃。
            Map(app.MapPost("/api/login", (Func<HttpContext, Task<IResult>>)Login), true);
            Map(app.MapPost("/api/logout", (HttpContext context) =>
            {
                context.Response.Cookies.Delete(TokenAuthenticator.SessionCookieName, new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Strict });
                return Ok(null);
            }), true);

            Map(app.MapGet("/api/displays", (HttpContext context) =>
                Ok(_monitorRegistry.GetMonitors(context.Request.Query["refresh"] == "true"))));
            Map(app.MapPost("/api/displays/rescan", () =>
            {
                _monitorRegistry.Rescan();
                return Ok(_monitorRegistry.GetMonitors(false));
            }));
            Map(app.MapPost("/api/displays/{id}/brightness", SetBrightness));

            Map(app.MapGet("/api/audio/volume", () => Ok(new { volume = _audioService.GetVolume() })));
            Map(app.MapPost("/api/audio/volume", async (HttpContext context) =>
            {
                _audioService.SetVolume(await RequireIntAsync(context, "value", 0, 100));
                return Ok(null);
            }));

            Map(app.MapPost("/api/power/lock", (HttpContext context) =>
            {
                _log.Info("Lock requested. Remote: " + Remote(context));
                _powerService.Lock();
                return Ok(null);
            }));
            Map(app.MapPost("/api/power/shutdown", (HttpContext context) =>
            {
                _log.Info("Shutdown requested. Remote: " + Remote(context));
                // 先把响应发出去再关机。
                context.Response.OnCompleted(() => Task.Run(_powerService.Shutdown));
                return Ok(null);
            }));
            Map(app.MapPost("/api/power/restart", (HttpContext context) =>
            {
                _log.Info("Restart requested. Remote: " + Remote(context));
                context.Response.OnCompleted(() => Task.Run(_powerService.Restart));
                return Ok(null);
            }));
            Map(app.MapGet("/api/power/schedule", () => Ok(ScheduleState())));
            Map(app.MapPost("/api/power/schedule", async (HttpContext context) =>
            {
                _shutdownScheduler.Schedule(TimeSpan.FromSeconds(await RequireIntAsync(context, "seconds", 0, 10 * 365 * 24 * 3600)));
                return Ok(ScheduleState());
            }));
            Map(app.MapDelete("/api/power/schedule", () =>
            {
                _shutdownScheduler.Cancel();
                return Ok(ScheduleState());
            }));
        }

        private static void Map(IEndpointConventionBuilder endpoint, bool isPublic = false)
        {
            endpoint.WithMetadata(new ApiEndpoint { IsPublic = isPublic });
        }

        /// <summary>非 GET 请求先过 CSRF 检查，再按端点检查登录状态。</summary>
        private async Task Guard(HttpContext context, Func<Task> next)
        {
            ApiEndpoint endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ApiEndpoint>();
            if (endpoint == null)
            {
                await next();
                return;
            }

            HttpRequest request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
                !RequestGuard.IsTrustedPost(request.Headers.Origin, request.Headers[RequestGuard.CsrfHeaderName], request.Headers.Host, request.Headers["X-Forwarded-Host"]))
            {
                _log.Warn("Rejected untrusted " + request.Method + " " + request.Path + ". Origin: " + request.Headers.Origin + " Remote: " + Remote(context));
                throw new ApiException(403, "Forbidden");
            }

            if (!endpoint.IsPublic && !IsAuthenticated(request))
            {
                if (!request.Path.StartsWithSegments("/api"))
                {
                    context.Response.Redirect("/login");
                    return;
                }
                throw new ApiException(401, "Unauthorized");
            }

            await next();
        }

        private async Task HandleErrors(HttpContext context, Func<Task> next)
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                var apiException = ex as ApiException;
                if (apiException == null)
                {
                    _log.Error("Process web request " + context.Request.Path + " failed.", ex);
                }
                context.Response.Clear();
                await WriteError(context, apiException?.StatusCode ?? 500, apiException?.Message ?? "Internal Server Error");
            }
        }

        /// <summary>路由系统产生的 404/405 等空响应：接口返回 JSON，页面返回纯文本。</summary>
        private static Task WriteStatusCodePage(StatusCodeContext statusContext)
        {
            HttpContext context = statusContext.HttpContext;
            string reason = ReasonPhrase(context.Response.StatusCode);
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                return WriteError(context, context.Response.StatusCode, reason);
            }
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(reason);
        }

        private static string ReasonPhrase(int statusCode)
        {
            return Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(statusCode);
        }

        private static Task WriteError(HttpContext context, int statusCode, string error)
        {
            context.Response.StatusCode = statusCode;
            context.Response.Headers.CacheControl = "no-store";
            return context.Response.WriteAsJsonAsync(new { ok = false, error });
        }

        private static IResult Ok(object data)
        {
            return Results.Json(new { ok = true, data });
        }

        private bool IsAuthenticated(HttpRequest request)
        {
            string session;
            if (request.Cookies.TryGetValue(TokenAuthenticator.SessionCookieName, out session) && _authenticator.IsValidSession(session))
            {
                return true;
            }

            // 供脚本、快捷指令等不方便处理 Cookie 的客户端使用，失败同样计入锁定次数。
            string headerToken = request.Headers[TokenAuthenticator.TokenHeaderName];
            return !string.IsNullOrEmpty(headerToken) && _authenticator.TryLogin(headerToken).Status == LoginStatus.Succeeded;
        }

        private async Task<IResult> Login(HttpContext context)
        {
            LoginResult result = _authenticator.TryLogin(await GetParameterAsync(context, "token"));
            switch (result.Status)
            {
                case LoginStatus.Succeeded:
                    context.Response.Cookies.Append(TokenAuthenticator.SessionCookieName, _authenticator.SessionValue, new CookieOptions
                    {
                        Path = "/",
                        MaxAge = TimeSpan.FromDays(30),
                        HttpOnly = true,
                        SameSite = SameSiteMode.Strict,
                        Secure = IsHttps(context.Request)
                    });
                    _log.Info("Login succeeded. Remote: " + Remote(context));
                    return Ok(null);
                case LoginStatus.Locked:
                    int seconds = (int)Math.Ceiling(result.RetryAfter.TotalSeconds);
                    _log.Warn("Login locked for " + seconds + "s. Remote: " + Remote(context));
                    context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    throw new ApiException(429, "Too Many Requests");
                default:
                    _log.Warn("Login failed. Remote: " + Remote(context));
                    throw new ApiException(401, "Unauthorized");
            }
        }

        private async Task<IResult> SetBrightness(HttpContext context, string id)
        {
            int brightness = await RequireIntAsync(context, "value", 0, 100);
            switch (_monitorRegistry.SetBrightness(id.ToLowerInvariant(), brightness))
            {
                case BrightnessWriteStatus.Applied:
                case BrightnessWriteStatus.Queued:
                    return Ok(null);
                case BrightnessWriteStatus.NotFound:
                    throw new ApiException(404, "Display not found");
                case BrightnessWriteStatus.NotSupported:
                    throw new ApiException(409, "Display does not support brightness control");
                default:
                    throw new ApiException(500, "Failed to set brightness");
            }
        }

        private object ScheduleState()
        {
            DateTime? deadline = _shutdownScheduler.Deadline;
            return new
            {
                remainingSeconds = _shutdownScheduler.RemainingSeconds,
                deadline = deadline.HasValue ? deadline.Value.ToString("o", CultureInfo.InvariantCulture) : null
            };
        }

        private static async Task<int> RequireIntAsync(HttpContext context, string name, int min, int max)
        {
            int value;
            if (!int.TryParse(await GetParameterAsync(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
            {
                throw new ApiException(400, "Parameter '" + name + "' must be an integer within " + min + " to " + max + ".");
            }
            return value;
        }

        /// <summary>表单参数优先，其次是查询字符串。</summary>
        private static async Task<string> GetParameterAsync(HttpContext context, string name)
        {
            if (context.Request.HasFormContentType)
            {
                IFormCollection form = await context.Request.ReadFormAsync();
                if (form.ContainsKey(name))
                {
                    return form[name];
                }
            }
            return context.Request.Query[name];
        }

        private static bool IsHttps(HttpRequest request)
        {
            return request.IsHttps || string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
        }

        private static string Remote(HttpContext context)
        {
            return context.Connection.RemoteIpAddress + ":" + context.Connection.RemotePort;
        }

        private static IResult Resource(string resourceName, string contentType)
        {
            byte[] content = LoadResource(resourceName);
            return content == null ? Results.NotFound() : Results.Bytes(content, contentType);
        }

        /// <summary>
        /// 读取嵌入的静态文件。DEBUG 构建优先读取源码目录中的 Web\wwwroot，改页面后刷新浏览器即可，不用重新编译。
        /// </summary>
        private static byte[] LoadResource(string resourceName)
        {
#if DEBUG
            const string wwwrootPrefix = "PCDeviceManage.wwwroot.";
            if (resourceName.StartsWith(wwwrootPrefix, StringComparison.Ordinal))
            {
                string fileName = resourceName.Substring(wwwrootPrefix.Length);
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string source = Path.Combine(dir.FullName, "Web", "wwwroot", fileName);
                    if (File.Exists(source))
                    {
                        return File.ReadAllBytes(source);
                    }
                }
            }
#endif
            using (Stream stream = typeof(WebConsole).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

    }
}
