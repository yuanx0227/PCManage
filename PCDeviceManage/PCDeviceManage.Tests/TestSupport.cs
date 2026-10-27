using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PCDeviceManage.Tests
{
    public static class TestSupport
    {
        /// <summary>
        /// 把测试输出 FakeFrpc 目录下的假 frpc 复制到 directory，exe 改名为 frpc.exe（它按内置的文件名找 FakeFrpc.dll）。
        /// </summary>
        public static void CopyFakeFrpc(string directory)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "FakeFrpc");
            foreach (string file in Directory.GetFiles(source, "FakeFrpc.*"))
            {
                string name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(directory, name == "FakeFrpc.exe" ? "frpc.exe" : name));
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

        /// <summary>HTTP 响应的状态码和头（响应头与内容头合并，多个值用逗号连接）。</summary>
        public sealed class TestResponse
        {
            private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public TestResponse(HttpResponseMessage response)
            {
                StatusCode = response.StatusCode;
                foreach (var header in response.Headers.Concat(response.Content.Headers))
                {
                    _headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            public HttpStatusCode StatusCode { get; }

            public IReadOnlyDictionary<string, string> Headers => _headers;
        }

        public sealed class TestServer : IDisposable
        {
            public const string Token = "integration-token";

            private readonly Microsoft.AspNetCore.Builder.WebApplication _app;
            private readonly MonitorRegistry _registry;
            private readonly HttpClient _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() });
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

                // 与正式程序使用同一个主机构建方法，只是服务换成测试替身；以实例注册的服务不归容器释放。
                _app = AppHost.Build("127.0.0.1", _port, services =>
                {
                    services.AddSingleton(_registry);
                    services.AddSingleton(new AudioService());
                    services.AddSingleton(new PowerService());
                    services.AddSingleton(Scheduler);
                    services.AddSingleton(new TokenAuthenticator(Token));
                });
                _app.StartAsync().GetAwaiter().GetResult();
            }

            public TestResponse Send(string method, string path, string form = null, IDictionary<string, string> headers = null, bool csrfHeader = true)
            {
                using (var request = new HttpRequestMessage(new HttpMethod(method), "http://127.0.0.1:" + _port + path))
                {
                    if (method != "GET" && csrfHeader)
                    {
                        request.Headers.TryAddWithoutValidation(RequestGuard.CsrfHeaderName, "1");
                    }
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                    if (form != null)
                    {
                        request.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }
                    else if (method != "GET")
                    {
                        request.Content = new ByteArrayContent(Array.Empty<byte>());
                    }

                    using (HttpResponseMessage response = _client.Send(request))
                    {
                        LastBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        return new TestResponse(response);
                    }
                }
            }

            public void Dispose()
            {
                _client.Dispose();
                _app.StopAsync().GetAwaiter().GetResult();
                _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
