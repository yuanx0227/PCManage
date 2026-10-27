using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Xunit;

namespace PCDeviceManage.Tests
{
    public static class TestSupport
    {
        /// <summary>
        /// 编译一个假的 frpc.exe：配置文件内容为 stay 时输出连接成功并保持运行，否则输出登录失败并退出。
        /// </summary>
        public static void CompileFakeFrpc(string outputPath)
        {
            const string source = @"
using System;
using System.IO;
using System.Threading;

static class FakeFrpc
{
    static int Main(string[] args)
    {
        string mode = File.ReadAllText(args[1]).Trim();
        if (mode == ""stay"")
        {
            Console.WriteLine(""2026/10/27 20:00:00 [I] [control.go:181] [test] [pcmanage] start proxy success"");
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return 0;
        }
        Console.WriteLine(""2026/10/27 20:00:00 [W] [service.go:104] login to server failed: test"");
        return 1;
    }
}";
            using (var provider = new Microsoft.CSharp.CSharpCodeProvider())
            {
                var parameters = new System.CodeDom.Compiler.CompilerParameters(new[] { "System.dll" }, outputPath)
                {
                    GenerateExecutable = true
                };
                var result = provider.CompileAssemblyFromSource(parameters, source);
                Assert.True(!result.Errors.HasErrors, "Failed to compile fake frpc: " + string.Join("; ", result.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(e => e.ErrorText)));
            }
        }

        public static MonitorRegistry CreateRegistry(params FakeMonitor[] monitors)
        {
            var registry = new MonitorRegistry(new FakeEnumerator(monitors.Cast<IMonitorDevice>().ToArray()));
            registry.Rescan();
            return registry;
        }

        public static bool WaitFor(Func<bool> condition)
        {
            var timeout = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < timeout)
            {
                if (condition())
                {
                    return true;
                }
                Thread.Sleep(50);
            }
            return condition();
        }

        public static bool IsRunning(int pid)
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public sealed class TempDirectory : IDisposable
        {
            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcmanage-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string File(string name)
            {
                return System.IO.Path.Combine(Path, name);
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }

        public sealed class TestServer : IDisposable
        {
            public const string Token = "integration-token";

            private readonly WebServer _server;
            private readonly MonitorRegistry _registry;
            private readonly CookieContainer _cookies = new CookieContainer();
            private readonly int _port;

            public string LastBody { get; private set; }
            public ShutdownScheduler Scheduler { get; }
            public int ShutdownCalls { get; private set; }

            public TestServer(MonitorRegistry registry = null)
            {
                _registry = registry ?? CreateRegistry(new FakeMonitor("aaaa0001", "Test Panel"));
                // 测试绝不能真的关机：关机动作只计数。
                Scheduler = new ShutdownScheduler(() => ShutdownCalls++);

                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                _server = new WebServer(
                    _registry,
                    new AudioService(),
                    new PowerService(),
                    Scheduler,
                    new TokenAuthenticator(Token),
                    _port,
                    "127.0.0.1",
                    log4net.LogManager.GetLogger("Tests"));
                Assert.True(_server.Start(), "Expected test web server to start.");
            }

            public HttpWebResponse Send(string method, string path, string form = null, IDictionary<string, string> headers = null, bool csrfHeader = true)
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
                request.Method = method;
                request.AllowAutoRedirect = false;
                request.CookieContainer = _cookies;
                if (method != "GET" && csrfHeader)
                {
                    request.Headers[RequestGuard.CsrfHeaderName] = "1";
                }
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers[header.Key] = header.Value;
                    }
                }
                if (form != null)
                {
                    byte[] body = Encoding.UTF8.GetBytes(form);
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.ContentLength = body.Length;
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(body, 0, body.Length);
                    }
                }
                else if (method != "GET")
                {
                    request.ContentLength = 0;
                }

                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    response = (HttpWebResponse)ex.Response;
                }

                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    LastBody = reader.ReadToEnd();
                }
                response.Close();
                return response;
            }

            public void Dispose()
            {
                _server.Dispose();
                _registry.Dispose();
                Scheduler.Dispose();
            }
        }

        public sealed class FakeMonitor : IMonitorDevice
        {
            public FakeMonitor(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }
            public string Name { get; }
            public bool IsInternal => false;
            public bool Supported { get; set; } = true;
            public bool IsBrightnessSupported => Supported;
            public int Brightness { get; private set; } = -1;
            public int HardwareBrightness { get; set; } = 50;
            public int RefreshCount { get; private set; }
            public bool IsDisposed { get; private set; }
            public List<int> Writes { get; } = new List<int>();
            public Func<int, bool> OnSet { get; set; }

            public void RefreshBrightness()
            {
                RefreshCount++;
                Brightness = HardwareBrightness;
            }

            public bool TrySetBrightness(int brightness)
            {
                if (IsDisposed || (OnSet != null && !OnSet(brightness)))
                {
                    return false;
                }
                Writes.Add(brightness);
                Brightness = brightness;
                return true;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        /// <summary>依次返回给定的显示器列表，用完后重复最后一组。</summary>
        public sealed class FakeEnumerator : IMonitorEnumerator
        {
            private readonly IMonitorDevice[][] _scans;

            public FakeEnumerator(params IMonitorDevice[][] scans)
            {
                _scans = scans;
            }

            public int Calls { get; private set; }

            public List<IMonitorDevice> Enumerate()
            {
                IMonitorDevice[] scan = _scans[Math.Min(Calls, _scans.Length - 1)];
                Calls++;
                return scan.ToList();
            }
        }
    }
}
