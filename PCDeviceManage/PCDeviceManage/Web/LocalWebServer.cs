using log4net;
using System;
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
        private readonly DisplayManage _displayManage;
        private readonly AudioManage _audioManage;
        private readonly WindowsManage _windowsManage;
        private readonly TokenAuthenticator _authenticator;
        private readonly int _port;
        private readonly string _host;
        private readonly ILog _log;
        private HttpListener _listener;
        private Task _listenTask;

        private static readonly object TimerLocker = new object();
        private static System.Timers.Timer _timer;
        private static int _shutdownTimingVal;
        private static int _elapsedSeconds;

        public LocalWebServer(
            DisplayManage displayManage,
            AudioManage audioManage,
            WindowsManage windowsManage,
            TokenAuthenticator authenticator,
            int port,
            string host,
            ILog log)
        {
            _displayManage = displayManage;
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
                if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    WriteText(context.Response, string.Empty);
                    return;
                }

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

                switch (path)
                {
                    case "/home/getdisplaydeviceinfo":
                        WriteText(context.Response, _displayManage.GetDisplayList());
                        break;
                    case "/home/setbrightness":
                        WriteText(context.Response, SetBrightness(context.Request).ToString());
                        break;
                    case "/home/getvolume":
                        WriteText(context.Response, _audioManage.GetVolume().ToString());
                        break;
                    case "/home/setvolume":
                        WriteText(context.Response, SetVolume(context.Request).ToString());
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
                        WriteText(context.Response, TimingShutdown(context.Request));
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

        private void Login(HttpListenerContext context)
        {
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                context.Response.AppendHeader("Allow", "POST");
                WriteText(context.Response, "Method Not Allowed");
                return;
            }

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

        private bool SetBrightness(HttpListenerRequest request)
        {
            int brightness;
            bool isInternal;
            if (!int.TryParse(request.QueryString["brightness"], out brightness))
            {
                brightness = 0;
            }
            if (!bool.TryParse(request.QueryString["IsInternal"], out isInternal))
            {
                isInternal = false;
            }
            return _displayManage.SetDisplayBrightness(brightness, isInternal);
        }

        private bool SetVolume(HttpListenerRequest request)
        {
            int volume;
            if (!int.TryParse(request.QueryString["volume"], out volume))
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

        private string TimingShutdown(HttpListenerRequest request)
        {
            int seconds;
            if (!int.TryParse(request.QueryString["minute"], out seconds))
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
