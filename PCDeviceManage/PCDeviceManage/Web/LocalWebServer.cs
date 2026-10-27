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
    public sealed class LocalWebServer : IDisposable
    {
        private readonly MonitorRegistry _monitorRegistry;
        private readonly AudioManage _audioManage;
        private readonly WindowsManage _windowsManage;
        private readonly TokenAuthenticator _authenticator;
        private readonly int _port;
        private readonly string _host;
        private readonly ILog _log;
        private HttpListener _listener;
        private Task _listenTask;

        // 会改变状态的接口只接受 POST，并经过 RequestGuard 校验。
        private static readonly HashSet<string> PostOnlyPaths = new HashSet<string>
        {
            "/api/login",
            "/api/logout",
            "/api/displays/rescan",
            "/home/setvolume",
            "/home/lockwindows",
            "/home/windowsshutdown",
            "/home/windowsrestart",
            "/home/timingshutdown"
        };

        private static readonly object TimerLocker = new object();
        private static System.Timers.Timer _timer;
        private static int _shutdownTimingVal;
        private static int _elapsedSeconds;

        public LocalWebServer(
            MonitorRegistry monitorRegistry,
            AudioManage audioManage,
            WindowsManage windowsManage,
            TokenAuthenticator authenticator,
            int port,
            string host,
            ILog log)
        {
            _monitorRegistry = monitorRegistry ?? throw new ArgumentNullException(nameof(monitorRegistry));
            _audioManage = audioManage;
            _windowsManage = windowsManage;
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _port = port;
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            _log = log;
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
            lock (TimerLocker)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }
            }
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
                    QueueRequest(context);
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

        private void QueueRequest(HttpListenerContext context)
        {
            Task.Run(() => ProcessRequest(context));
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                if (path == "/favicon.ico")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                if (path == "/login")
                {
                    WriteHtml(context.Response, LoginPage.Html);
                    return;
                }

                if (IsPostOnly(path) && !IsAllowedPost(context))
                {
                    return;
                }

                if (path == "/api/login")
                {
                    Login(context);
                    return;
                }

                if (path == "/api/logout")
                {
                    context.Response.AppendHeader("Set-Cookie", TokenAuthenticator.SessionCookieName + "=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict");
                    WriteText(context.Response, "true");
                    return;
                }

                bool isPage = string.IsNullOrEmpty(path) || path == "/home" || path == "/home/index";
                if (!IsAuthenticated(context.Request))
                {
                    if (isPage)
                    {
                        context.Response.Redirect("/login");
                        context.Response.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 401;
                        WriteText(context.Response, "Unauthorized");
                    }
                    return;
                }

                if (isPage)
                {
                    WriteHtml(context.Response, IndexPage.Html);
                    return;
                }

                if (IsDisplayBrightnessPath(path))
                {
                    SetBrightness(context, path.Substring(DisplaysPrefix.Length, path.Length - DisplaysPrefix.Length - BrightnessSuffix.Length));
                    return;
                }

                switch (path)
                {
                    case "/api/displays":
                        WriteJson(context.Response, _monitorRegistry.GetMonitors(context.Request.QueryString["refresh"] == "true"));
                        break;
                    case "/api/displays/rescan":
                        _monitorRegistry.Rescan();
                        WriteJson(context.Response, _monitorRegistry.GetMonitors(false));
                        break;
                    case "/home/getvolume":
                        WriteText(context.Response, _audioManage.GetVolume().ToString());
                        break;
                    case "/home/setvolume":
                        WriteText(context.Response, SetVolume(ReadParameters(context.Request)).ToString());
                        break;
                    case "/home/lockwindows":
                        WriteText(context.Response, LockWindows(context.Request));
                        break;
                    case "/home/windowsshutdown":
                        _windowsManage.WindowsShutdown();
                        WriteText(context.Response, "true");
                        break;
                    case "/home/windowsrestart":
                        _windowsManage.WindowsRestart();
                        WriteText(context.Response, "true");
                        break;
                    case "/home/timingshutdown":
                        WriteText(context.Response, TimingShutdown(ReadParameters(context.Request)));
                        break;
                    case "/home/getshutdowntimingval":
                        WriteText(context.Response, GetShutdownTimingVal());
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        WriteText(context.Response, "Not Found");
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Process web request failed.", ex);
                context.Response.StatusCode = 500;
                WriteText(context.Response, "0");
            }
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

        private const string DisplaysPrefix = "/api/displays/";
        private const string BrightnessSuffix = "/brightness";

        private static bool IsDisplayBrightnessPath(string path)
        {
            return path.StartsWith(DisplaysPrefix, StringComparison.Ordinal) &&
                path.EndsWith(BrightnessSuffix, StringComparison.Ordinal) &&
                path.Length > DisplaysPrefix.Length + BrightnessSuffix.Length;
        }

        private static bool IsPostOnly(string path)
        {
            return PostOnlyPaths.Contains(path) || IsDisplayBrightnessPath(path);
        }

        private bool IsAllowedPost(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                context.Response.AppendHeader("Allow", "POST");
                WriteText(context.Response, "Method Not Allowed");
                return false;
            }

            if (!RequestGuard.IsTrustedPost(
                request.Headers["Origin"],
                request.Headers[RequestGuard.CsrfHeaderName],
                request.Headers["Host"],
                request.Headers["X-Forwarded-Host"]))
            {
                _log.Warn("Rejected untrusted POST " + request.Url.AbsolutePath + ". Origin: " + request.Headers["Origin"] + " Remote: " + request.RemoteEndPoint);
                context.Response.StatusCode = 403;
                WriteText(context.Response, "Forbidden");
                return false;
            }

            return true;
        }

        private void Login(HttpListenerContext context)
        {
            LoginResult result = _authenticator.TryLogin(ReadForm(context.Request)["token"]);
            switch (result.Status)
            {
                case LoginStatus.Succeeded:
                    string secure = IsHttps(context.Request) ? "; Secure" : string.Empty;
                    context.Response.AppendHeader("Set-Cookie",
                        TokenAuthenticator.SessionCookieName + "=" + _authenticator.SessionValue +
                        "; Path=/; Max-Age=2592000; HttpOnly; SameSite=Strict" + secure);
                    _log.Info("Login succeeded. Remote: " + context.Request.RemoteEndPoint);
                    WriteText(context.Response, "true");
                    break;
                case LoginStatus.Locked:
                    int seconds = (int)Math.Ceiling(result.RetryAfter.TotalSeconds);
                    _log.Warn("Login locked for " + seconds + "s. Remote: " + context.Request.RemoteEndPoint);
                    context.Response.StatusCode = 429;
                    context.Response.AppendHeader("Retry-After", seconds.ToString(CultureInfo.InvariantCulture));
                    WriteText(context.Response, "Too Many Requests");
                    break;
                default:
                    _log.Warn("Login failed. Remote: " + context.Request.RemoteEndPoint);
                    context.Response.StatusCode = 401;
                    WriteText(context.Response, "Unauthorized");
                    break;
            }
        }

        private static bool IsHttps(HttpListenerRequest request)
        {
            return request.IsSecureConnection ||
                string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 合并查询字符串和表单参数，表单优先。
        /// </summary>
        private static NameValueCollection ReadParameters(HttpListenerRequest request)
        {
            var parameters = new NameValueCollection(request.QueryString);
            NameValueCollection form = ReadForm(request);
            foreach (string key in form)
            {
                parameters[key] = form[key];
            }
            return parameters;
        }

        private static NameValueCollection ReadForm(HttpListenerRequest request)
        {
            var form = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            if (!request.HasEntityBody)
            {
                return form;
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
                form[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return form;
        }

        private void SetBrightness(HttpListenerContext context, string id)
        {
            int brightness;
            if (!int.TryParse(ReadParameters(context.Request)["value"], out brightness) || brightness < 0 || brightness > 100)
            {
                context.Response.StatusCode = 400;
                WriteText(context.Response, "Brightness must be an integer within 0 to 100.");
                return;
            }

            BrightnessWriteStatus status = _monitorRegistry.SetBrightness(id, brightness);
            switch (status)
            {
                case BrightnessWriteStatus.Applied:
                case BrightnessWriteStatus.Queued:
                    WriteText(context.Response, "true");
                    break;
                case BrightnessWriteStatus.NotFound:
                    context.Response.StatusCode = 404;
                    WriteText(context.Response, "Display not found");
                    break;
                case BrightnessWriteStatus.NotSupported:
                    context.Response.StatusCode = 409;
                    WriteText(context.Response, "Display does not support brightness control");
                    break;
                default:
                    context.Response.StatusCode = 500;
                    WriteText(context.Response, "false");
                    break;
            }
        }

        private bool SetVolume(NameValueCollection parameters)
        {
            int volume;
            if (!int.TryParse(parameters["volume"], out volume))
            {
                volume = 0;
            }
            return _audioManage.SetAudioVolume(volume, string.Empty);
        }

        private string LockWindows(HttpListenerRequest request)
        {
            _windowsManage.LockWindows();
            _log.Info("Lock command sent. Remote: " + request.RemoteEndPoint);
            return "true";
        }

        private string TimingShutdown(NameValueCollection parameters)
        {
            // 旧参数名 minute 实际传的就是秒数，保留兼容。
            int seconds;
            if (!int.TryParse(parameters["seconds"] ?? parameters["minute"], out seconds))
            {
                seconds = 0;
            }

            lock (TimerLocker)
            {
                EnsureTimer();
                _elapsedSeconds = 0;

                if (seconds <= 0)
                {
                    _timer.Stop();
                    _shutdownTimingVal = 0;
                }
                else
                {
                    _shutdownTimingVal = seconds;
                    _timer.Start();
                }
            }

            return "true";
        }

        private void EnsureTimer()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new System.Timers.Timer(1000);
            _timer.AutoReset = true;
            _timer.Elapsed += TimerUp;
        }

        private void TimerUp(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (TimerLocker)
            {
                _elapsedSeconds++;
                if (_elapsedSeconds >= _shutdownTimingVal && _shutdownTimingVal > 0)
                {
                    _timer.Stop();
                    _shutdownTimingVal = 0;
                    _elapsedSeconds = 0;
                    _windowsManage.WindowsShutdown();
                }
            }
        }

        private string GetShutdownTimingVal()
        {
            lock (TimerLocker)
            {
                int remaining = _shutdownTimingVal == 0 ? 0 : _shutdownTimingVal - _elapsedSeconds;
                return remaining.ToString();
            }
        }

        private static void WriteHtml(HttpListenerResponse response, string content)
        {
            Write(response, content, "text/html; charset=utf-8");
        }

        private static void WriteJson(HttpListenerResponse response, object value)
        {
            Write(response, JsonConvert.SerializeObject(value), "application/json; charset=utf-8");
        }

        private static void WriteText(HttpListenerResponse response, string content)
        {
            Write(response, content ?? string.Empty, "text/plain; charset=utf-8");
        }

        private static void Write(HttpListenerResponse response, string content, string contentType)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.ContentType = contentType;
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
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
