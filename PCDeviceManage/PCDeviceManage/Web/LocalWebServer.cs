using log4net;
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCDeviceManage
{
    public sealed class LocalWebServer : IDisposable
    {
        private readonly DisplayManage _displayManage;
        private readonly AudioManage _audioManage;
        private readonly WindowsManage _windowsManage;
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
            int port,
            string host,
            ILog log)
        {
            _displayManage = displayManage;
            _audioManage = audioManage;
            _windowsManage = windowsManage;
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
                if (string.IsNullOrEmpty(path) || path == "/home" || path == "/home/index")
                {
                    WriteHtml(context.Response, IndexPage.Html);
                    return;
                }

                if (path == "/favicon.ico")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
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
            string ip = GetIp(request);
            _log.Info("Lock command sent. Client IP: " + ip + " Mac: " + GetMac(ip));
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

        private string GetIp(HttpListenerRequest request)
        {
            string customerIp = request.Headers["Cdn-Src-Ip"];
            if (!string.IsNullOrWhiteSpace(customerIp))
            {
                return customerIp;
            }

            customerIp = request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrWhiteSpace(customerIp))
            {
                int commaIndex = customerIp.IndexOf(',');
                return commaIndex > 0 ? customerIp.Substring(0, commaIndex).Trim() : customerIp;
            }

            return request.RemoteEndPoint == null ? string.Empty : request.RemoteEndPoint.Address.ToString();
        }

        [DllImport("Iphlpapi.dll")]
        private static extern int SendARP(Int32 destIp, Int32 srcIp, ref Int64 macAddr, ref Int32 phyAddrLen);

        [DllImport("Ws2_32.dll")]
        private static extern Int32 inet_addr(string ipaddr);

        private string GetMac(string remoteIp)
        {
            StringBuilder macAddress = new StringBuilder();
            try
            {
                Int32 remote = inet_addr(remoteIp);
                Int64 macInfo = new Int64();
                Int32 length = 6;
                SendARP(remote, 0, ref macInfo, ref length);
                string temp = Convert.ToString(macInfo, 16).PadLeft(12, '0').ToUpper();
                int x = 12;
                for (int i = 0; i < 6; i++)
                {
                    if (i == 5)
                    {
                        macAddress.Append(temp.Substring(x - 2, 2));
                    }
                    else
                    {
                        macAddress.Append(temp.Substring(x - 2, 2) + "-");
                    }
                    x -= 2;
                }
            }
            catch
            {
            }
            return macAddress.ToString();
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
