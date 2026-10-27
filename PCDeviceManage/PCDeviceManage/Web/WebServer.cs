using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PCDeviceManage
{
    /// <summary>
    /// 本机 Web 控制台。只负责监听、路由、鉴权、防 CSRF 和 JSON 序列化，具体操作交给各个服务。
    /// 接口统一返回 { "ok": true, "data": ... } 或 { "ok": false, "error": "..." }。
    /// </summary>
    public sealed class WebServer : IDisposable
    {
        private delegate void Handler(RequestContext context);

        private sealed class Route
        {
            public Handler Handler;
            public bool IsPublic;
        }

        private sealed class RequestContext
        {
            private NameValueCollection _parameters;

            public HttpListenerContext Http;
            public string RouteId;

            public HttpListenerRequest Request => Http.Request;
            public HttpListenerResponse Response => Http.Response;

            /// <summary>查询字符串和表单参数（表单优先），首次访问时读取请求体。</summary>
            public NameValueCollection Parameters => _parameters ?? (_parameters = ReadParameters(Http.Request));
        }

        private sealed class ApiException : Exception
        {
            public ApiException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }

            public int StatusCode { get; }
        }

        private const string DisplaysPrefix = "/api/displays/";
        private const string BrightnessSuffix = "/brightness";
        private const string BrightnessRoute = "/api/displays/{id}/brightness";

        private readonly MonitorRegistry _monitorRegistry;
        private readonly AudioService _audioService;
        private readonly PowerService _powerService;
        private readonly ShutdownScheduler _shutdownScheduler;
        private readonly TokenAuthenticator _authenticator;
        private readonly int _port;
        private readonly string _host;
        private readonly ILog _log;
        // 路径 -> (HTTP 方法 -> 路由)
        private readonly Dictionary<string, Dictionary<string, Route>> _routes = new Dictionary<string, Dictionary<string, Route>>();
        private HttpListener _listener;
        private Task _listenTask;

        public WebServer(
            MonitorRegistry monitorRegistry,
            AudioService audioService,
            PowerService powerService,
            ShutdownScheduler shutdownScheduler,
            TokenAuthenticator authenticator,
            int port,
            string host,
            ILog log)
        {
            _monitorRegistry = monitorRegistry ?? throw new ArgumentNullException(nameof(monitorRegistry));
            _audioService = audioService;
            _powerService = powerService;
            _shutdownScheduler = shutdownScheduler ?? throw new ArgumentNullException(nameof(shutdownScheduler));
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _port = port;
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            _log = log ?? LogManager.GetLogger(typeof(WebServer));

            Map("GET", "/favicon.ico", c => WriteResource(c.Response, "PCDeviceManage.app.ico", "image/x-icon"), true);
            Map("GET", "/login", c => WriteResource(c.Response, "PCDeviceManage.wwwroot.login.html", "text/html; charset=utf-8"), true);
            Map("GET", "", c => WriteResource(c.Response, "PCDeviceManage.wwwroot.index.html", "text/html; charset=utf-8"));
            Map("POST", "/api/login", Login, true);
            Map("POST", "/api/logout", Logout, true);

            Map("GET", "/api/displays", c => Ok(c, _monitorRegistry.GetMonitors(c.Request.QueryString["refresh"] == "true")));
            Map("POST", "/api/displays/rescan", c =>
            {
                _monitorRegistry.Rescan();
                Ok(c, _monitorRegistry.GetMonitors(false));
            });
            Map("POST", BrightnessRoute, SetBrightness);

            Map("GET", "/api/audio/volume", c => Ok(c, new { volume = _audioService.GetVolume() }));
            Map("POST", "/api/audio/volume", c =>
            {
                _audioService.SetVolume(RequireInt(c, "value", 0, 100));
                Ok(c, null);
            });

            Map("POST", "/api/power/lock", c =>
            {
                _log.Info("Lock requested. Remote: " + c.Request.RemoteEndPoint);
                _powerService.Lock();
                Ok(c, null);
            });
            Map("POST", "/api/power/shutdown", c =>
            {
                _log.Info("Shutdown requested. Remote: " + c.Request.RemoteEndPoint);
                Ok(c, null);
                _powerService.Shutdown();
            });
            Map("POST", "/api/power/restart", c =>
            {
                _log.Info("Restart requested. Remote: " + c.Request.RemoteEndPoint);
                Ok(c, null);
                _powerService.Restart();
            });
            Map("GET", "/api/power/schedule", c => Ok(c, ScheduleState()));
            Map("POST", "/api/power/schedule", c =>
            {
                _shutdownScheduler.Schedule(TimeSpan.FromSeconds(RequireInt(c, "seconds", 0, 10 * 365 * 24 * 3600)));
                Ok(c, ScheduleState());
            });
            Map("DELETE", "/api/power/schedule", c =>
            {
                _shutdownScheduler.Cancel();
                Ok(c, ScheduleState());
            });
        }

        public bool Start()
        {
            if (_listener != null && _listener.IsListening)
            {
                return true;
            }

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(BuildPrefix());
                _listener.Start();
                _listenTask = Task.Run((Func<Task>)ListenAsync);
                _log.Info("Web server started: " + BuildPrefix());
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Web server start failed.", ex);
                DisposeListener();
                return false;
            }
        }

        public void Stop()
        {
            DisposeListener();
        }

        private void Map(string method, string path, Handler handler, bool isPublic = false)
        {
            Dictionary<string, Route> byMethod;
            if (!_routes.TryGetValue(path, out byMethod))
            {
                byMethod = new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);
                _routes[path] = byMethod;
            }
            byMethod[method] = new Route { Handler = handler, IsPublic = isPublic };
        }

        private string BuildPrefix()
        {
            string host = _host == "*" ? "+" : _host;
            return "http://" + host + ":" + _port + "/";
        }

        private async Task ListenAsync()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Error("Accept web request failed.", ex);
                }
            }
        }

        private void ProcessRequest(HttpListenerContext http)
        {
            var context = new RequestContext { Http = http };
            string path = http.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            bool isApi = path.StartsWith("/api/", StringComparison.Ordinal);

            try
            {
                Dictionary<string, Route> byMethod;
                string routePath = ResolveRoute(path, context);
                if (routePath == null || !_routes.TryGetValue(routePath, out byMethod))
                {
                    Fail(context, 404, "Not Found", isApi);
                    return;
                }

                Route route;
                if (!byMethod.TryGetValue(http.Request.HttpMethod, out route))
                {
                    http.Response.AppendHeader("Allow", string.Join(", ", byMethod.Keys));
                    Fail(context, 405, "Method Not Allowed", isApi);
                    return;
                }

                if (!IsSafeMethod(http.Request.HttpMethod) && !IsTrustedRequest(http.Request))
                {
                    _log.Warn("Rejected untrusted " + http.Request.HttpMethod + " " + path + ". Origin: " + http.Request.Headers["Origin"] + " Remote: " + http.Request.RemoteEndPoint);
                    Fail(context, 403, "Forbidden", isApi);
                    return;
                }

                if (!route.IsPublic && !IsAuthenticated(http.Request))
                {
                    if (isApi)
                    {
                        Fail(context, 401, "Unauthorized", true);
                    }
                    else
                    {
                        http.Response.Redirect("/login");
                        http.Response.Close();
                    }
                    return;
                }

                route.Handler(context);
            }
            catch (ApiException ex)
            {
                Fail(context, ex.StatusCode, ex.Message, isApi);
            }
            catch (Exception ex)
            {
                _log.Error("Process web request " + path + " failed.", ex);
                Fail(context, 500, "Internal Server Error", isApi);
            }
        }

        /// <summary>把请求路径映射到路由表中的路径；/home 等旧地址仍然打开首页。</summary>
        private static string ResolveRoute(string path, RequestContext context)
        {
            if (path == "/home" || path == "/home/index")
            {
                return string.Empty;
            }

            if (path.StartsWith(DisplaysPrefix, StringComparison.Ordinal) &&
                path.EndsWith(BrightnessSuffix, StringComparison.Ordinal) &&
                path.Length > DisplaysPrefix.Length + BrightnessSuffix.Length)
            {
                context.RouteId = path.Substring(DisplaysPrefix.Length, path.Length - DisplaysPrefix.Length - BrightnessSuffix.Length);
                return BrightnessRoute;
            }

            return path;
        }

        private static bool IsSafeMethod(string method)
        {
            return method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedRequest(HttpListenerRequest request)
        {
            return RequestGuard.IsTrustedPost(
                request.Headers["Origin"],
                request.Headers[RequestGuard.CsrfHeaderName],
                request.Headers["Host"],
                request.Headers["X-Forwarded-Host"]);
        }

        private bool IsAuthenticated(HttpListenerRequest request)
        {
            Cookie cookie = request.Cookies[TokenAuthenticator.SessionCookieName];
            if (cookie != null && _authenticator.IsValidSession(cookie.Value))
            {
                return true;
            }

            // 供脚本、快捷指令等不方便处理 Cookie 的客户端使用，失败同样计入锁定次数。
            string headerToken = request.Headers[TokenAuthenticator.TokenHeaderName];
            return !string.IsNullOrEmpty(headerToken) && _authenticator.TryLogin(headerToken).Status == LoginStatus.Succeeded;
        }

        private void Login(RequestContext context)
        {
            LoginResult result = _authenticator.TryLogin(context.Parameters["token"]);
            switch (result.Status)
            {
                case LoginStatus.Succeeded:
                    string secure = IsHttps(context.Request) ? "; Secure" : string.Empty;
                    context.Response.AppendHeader("Set-Cookie",
                        TokenAuthenticator.SessionCookieName + "=" + _authenticator.SessionValue +
                        "; Path=/; Max-Age=2592000; HttpOnly; SameSite=Strict" + secure);
                    _log.Info("Login succeeded. Remote: " + context.Request.RemoteEndPoint);
                    Ok(context, null);
                    break;
                case LoginStatus.Locked:
                    int seconds = (int)Math.Ceiling(result.RetryAfter.TotalSeconds);
                    _log.Warn("Login locked for " + seconds + "s. Remote: " + context.Request.RemoteEndPoint);
                    context.Response.AppendHeader("Retry-After", seconds.ToString(CultureInfo.InvariantCulture));
                    Fail(context, 429, "Too Many Requests", true);
                    break;
                default:
                    _log.Warn("Login failed. Remote: " + context.Request.RemoteEndPoint);
                    Fail(context, 401, "Unauthorized", true);
                    break;
            }
        }

        private void Logout(RequestContext context)
        {
            context.Response.AppendHeader("Set-Cookie", TokenAuthenticator.SessionCookieName + "=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict");
            Ok(context, null);
        }

        private void SetBrightness(RequestContext context)
        {
            int brightness = RequireInt(context, "value", 0, 100);
            switch (_monitorRegistry.SetBrightness(context.RouteId, brightness))
            {
                case BrightnessWriteStatus.Applied:
                case BrightnessWriteStatus.Queued:
                    Ok(context, null);
                    break;
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

        private static int RequireInt(RequestContext context, string name, int min, int max)
        {
            int value;
            if (!int.TryParse(context.Parameters[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
            {
                throw new ApiException(400, "Parameter '" + name + "' must be an integer within " + min + " to " + max + ".");
            }
            return value;
        }

        private static bool IsHttps(HttpListenerRequest request)
        {
            return request.IsSecureConnection ||
                string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
        }

        private static NameValueCollection ReadParameters(HttpListenerRequest request)
        {
            var parameters = new NameValueCollection(request.QueryString);
            if (!request.HasEntityBody)
            {
                return parameters;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                char[] buffer = new char[8192];
                int length = reader.ReadBlock(buffer, 0, buffer.Length);
                body = new string(buffer, 0, length);
            }

            foreach (string pair in body.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                int index = pair.IndexOf('=');
                string key = index < 0 ? pair : pair.Substring(0, index);
                string value = index < 0 ? string.Empty : pair.Substring(index + 1);
                parameters[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return parameters;
        }

        private static void Ok(RequestContext context, object data)
        {
            WriteJson(context.Response, 200, new { ok = true, data });
        }

        private static void Fail(RequestContext context, int statusCode, string error, bool json)
        {
            if (json)
            {
                WriteJson(context.Response, statusCode, new { ok = false, error });
            }
            else
            {
                context.Response.StatusCode = statusCode;
                Write(context.Response, Encoding.UTF8.GetBytes(error), "text/plain; charset=utf-8");
            }
        }

        private static void WriteJson(HttpListenerResponse response, int statusCode, object value)
        {
            response.StatusCode = statusCode;
            response.AddHeader("Cache-Control", "no-store");
            Write(response, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)), "application/json; charset=utf-8");
        }

        private static void WriteResource(HttpListenerResponse response, string resourceName, string contentType)
        {
            byte[] content = LoadResource(resourceName);
            if (content == null)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }
            response.AddHeader("Cache-Control", "no-cache");
            Write(response, content, contentType);
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
                string source = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Web\wwwroot", resourceName.Substring(wwwrootPrefix.Length)));
                if (File.Exists(source))
                {
                    return File.ReadAllBytes(source);
                }
            }
#endif
            using (Stream stream = typeof(WebServer).Assembly.GetManifestResourceStream(resourceName))
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

        private static void Write(HttpListenerResponse response, byte[] content, string contentType)
        {
            response.ContentType = contentType;
            response.ContentLength64 = content.Length;
            response.OutputStream.Write(content, 0, content.Length);
            response.Close();
        }

        private void DisposeListener()
        {
            if (_listener == null)
            {
                return;
            }

            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
            }
            finally
            {
                _listener = null;
                _listenTask = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
