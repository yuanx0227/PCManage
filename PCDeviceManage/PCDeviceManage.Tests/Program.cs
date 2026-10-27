using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCDeviceManage.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                Test(nameof(MonitorIdIsStableAndUrlSafe), MonitorIdIsStableAndUrlSafe),
                Test(nameof(RegistryRoutesBrightnessById), RegistryRoutesBrightnessById),
                Test(nameof(RegistryReadsHardwareOnlyWhenAsked), RegistryReadsHardwareOnlyWhenAsked),
                Test(nameof(RescanDisposesReplacedMonitors), RescanDisposesReplacedMonitors),
                Test(nameof(FailedWriteRescansAndRetries), FailedWriteRescansAndRetries),
                Test(nameof(UnknownAndUnsupportedMonitorsAreRejected), UnknownAndUnsupportedMonitorsAreRejected),
                Test(nameof(ConcurrentWritesKeepLatestValue), ConcurrentWritesKeepLatestValue),
                Test(nameof(WebServerSetsBrightnessById), WebServerSetsBrightnessById),
                Test(nameof(LoginAcceptsOnlyCorrectToken), LoginAcceptsOnlyCorrectToken),
                Test(nameof(LoginLocksAfterRepeatedFailures), LoginLocksAfterRepeatedFailures),
                Test(nameof(SuccessfulLoginResetsFailureCount), SuccessfulLoginResetsFailureCount),
                Test(nameof(SessionValueIsBoundToToken), SessionValueIsBoundToToken),
                Test(nameof(TokenIsGeneratedOnceAndReused), TokenIsGeneratedOnceAndReused),
                Test(nameof(WebServerRequiresLogin), WebServerRequiresLogin),
                Test(nameof(WebServerAcceptsTokenHeader), WebServerAcceptsTokenHeader),
                Test(nameof(RequestGuardRequiresHeaderAndSameOrigin), RequestGuardRequiresHeaderAndSameOrigin),
                Test(nameof(WebServerRejectsUnsafeStateChanges), WebServerRejectsUnsafeStateChanges),
            };

            int failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    test.Value();
                    Console.WriteLine("PASS " + test.Key);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + test.Key + ": " + ex.Message);
                }
            }

            Console.WriteLine(failed == 0 ? "All " + tests.Count + " tests passed." : failed + " of " + tests.Count + " tests failed.");
            return failed == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action> Test(string name, Action action)
        {
            return new KeyValuePair<string, Action>(name, action);
        }

        private static void MonitorIdIsStableAndUrlSafe()
        {
            string id = MonitorIds.FromDeviceInstanceId(@"DISPLAY\DELA0A1\5&2B2E1F0&0&UID4353");
            Assert(id == MonitorIds.FromDeviceInstanceId(@"display\dela0a1\5&2b2e1f0&0&uid4353"), "Expected id to ignore case.");
            Assert(id.Length == 8 && id.All(c => "0123456789abcdef".IndexOf(c) >= 0), "Expected an 8-char hex id.");
            Assert(id != MonitorIds.FromDeviceInstanceId(@"DISPLAY\DELA0A1\5&2B2E1F0&0&UID4354"), "Expected different monitors to get different ids.");
        }

        private static void RegistryRoutesBrightnessById()
        {
            var a = new FakeMonitor("aaaa0001", "External A");
            var b = new FakeMonitor("bbbb0002", "External B");
            using (var registry = CreateRegistry(a, b))
            {
                Assert(registry.SetBrightness("bbbb0002", 30) == BrightnessWriteStatus.Applied, "Expected write to B to apply.");
                Assert(registry.SetBrightness("aaaa0001", 70) == BrightnessWriteStatus.Applied, "Expected write to A to apply.");

                Assert(a.Writes.SequenceEqual(new[] { 70 }), "Expected A to receive only its own value.");
                Assert(b.Writes.SequenceEqual(new[] { 30 }), "Expected B to receive only its own value.");

                var infos = registry.GetMonitors(false);
                Assert(infos.Single(x => x.Id == "aaaa0001").Brightness == 70, "Expected A brightness to be reported.");
                Assert(infos.Single(x => x.Id == "bbbb0002").Brightness == 30, "Expected B brightness to be reported.");
            }
        }

        private static void RegistryReadsHardwareOnlyWhenAsked()
        {
            var monitor = new FakeMonitor("aaaa0001", "External A");
            using (var registry = CreateRegistry(monitor))
            {
                int afterScan = monitor.RefreshCount;
                Assert(afterScan == 1, "Expected rescan to read brightness once.");

                registry.GetMonitors(false);
                Assert(monitor.RefreshCount == afterScan, "Expected cached read not to touch hardware.");

                monitor.HardwareBrightness = 55;
                var refreshed = registry.GetMonitors(true);
                Assert(monitor.RefreshCount == afterScan + 1 && refreshed[0].Brightness == 55, "Expected refresh to read hardware.");
            }
        }

        private static void RescanDisposesReplacedMonitors()
        {
            var first = new FakeMonitor("aaaa0001", "External A");
            var second = new FakeMonitor("aaaa0001", "External A");
            var enumerator = new FakeEnumerator(new IMonitorDevice[] { first }, new IMonitorDevice[] { second });
            var registry = new MonitorRegistry(enumerator);
            registry.Rescan();
            registry.Rescan();

            Assert(first.IsDisposed && !second.IsDisposed, "Expected the replaced monitor to be disposed.");
            Assert(registry.SetBrightness("aaaa0001", 40) == BrightnessWriteStatus.Applied && second.Writes.SequenceEqual(new[] { 40 }), "Expected writes to go to the new monitor.");

            registry.Dispose();
            Assert(second.IsDisposed, "Expected registry dispose to release monitors.");
        }

        private static void FailedWriteRescansAndRetries()
        {
            var stale = new FakeMonitor("aaaa0001", "External A") { OnSet = value => false };
            var fresh = new FakeMonitor("aaaa0001", "External A");
            var enumerator = new FakeEnumerator(new IMonitorDevice[] { stale }, new IMonitorDevice[] { fresh });
            using (var registry = new MonitorRegistry(enumerator))
            {
                registry.Rescan();
                Assert(registry.SetBrightness("aaaa0001", 25) == BrightnessWriteStatus.Applied, "Expected write to succeed after rescan.");
                Assert(enumerator.Calls == 2, "Expected a failed write to trigger exactly one rescan.");
                Assert(stale.IsDisposed && fresh.Writes.SequenceEqual(new[] { 25 }), "Expected the retry to use the rescanned monitor.");
            }
        }

        private static void UnknownAndUnsupportedMonitorsAreRejected()
        {
            var unsupported = new FakeMonitor("cccc0003", "Old Monitor") { Supported = false };
            using (var registry = CreateRegistry(unsupported))
            {
                Assert(registry.SetBrightness("ffffffff", 50) == BrightnessWriteStatus.NotFound, "Expected unknown id to be reported.");
                Assert(registry.SetBrightness("cccc0003", 50) == BrightnessWriteStatus.NotSupported, "Expected unsupported monitor to be reported.");
                Assert(unsupported.Writes.Count == 0, "Expected no write to an unsupported monitor.");
                Assert(!registry.GetMonitors(false)[0].Supported, "Expected unsupported flag in the listing.");
            }
        }

        private static void ConcurrentWritesKeepLatestValue()
        {
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var monitor = new FakeMonitor("aaaa0001", "External A")
            {
                OnSet = value =>
                {
                    if (value == 10)
                    {
                        entered.Set();
                        release.Wait();
                    }
                    return true;
                }
            };

            using (var registry = CreateRegistry(monitor))
            {
                Task<BrightnessWriteStatus> first = Task.Run(() => registry.SetBrightness("aaaa0001", 10));
                Assert(entered.Wait(5000), "Expected first write to start.");

                Assert(registry.SetBrightness("aaaa0001", 20) == BrightnessWriteStatus.Queued, "Expected second write to be queued.");
                Assert(registry.SetBrightness("aaaa0001", 30) == BrightnessWriteStatus.Queued, "Expected third write to be queued.");
                release.Set();

                Assert(first.Wait(5000) && first.Result == BrightnessWriteStatus.Applied, "Expected the writer to finish.");
                Assert(monitor.Writes.SequenceEqual(new[] { 10, 30 }), "Expected intermediate value to be skipped, got " + string.Join(",", monitor.Writes));
            }
        }

        private static void WebServerSetsBrightnessById()
        {
            var a = new FakeMonitor("aaaa0001", "External A");
            var b = new FakeMonitor("bbbb0002", "External B");
            using (var server = new TestServer(CreateRegistry(a, b)))
            {
                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));

                Assert(server.Send("GET", "/api/displays").StatusCode == HttpStatusCode.OK, "Expected display listing to succeed.");
                Assert(server.LastBody.Contains("\"id\":\"bbbb0002\"") && server.LastBody.Contains("\"supported\":true"), "Expected listing JSON with ids, got " + server.LastBody);

                Assert(server.Send("POST", "/api/displays/bbbb0002/brightness", "value=45").StatusCode == HttpStatusCode.OK, "Expected brightness write to succeed.");
                Assert(b.Writes.SequenceEqual(new[] { 45 }) && a.Writes.Count == 0, "Expected only display B to change.");

                Assert(server.Send("POST", "/api/displays/ffffffff/brightness", "value=45").StatusCode == HttpStatusCode.NotFound, "Expected unknown display to return 404.");
                Assert(server.Send("POST", "/api/displays/bbbb0002/brightness", "value=101").StatusCode == HttpStatusCode.BadRequest, "Expected out-of-range value to return 400.");
                Assert(server.Send("GET", "/api/displays/bbbb0002/brightness").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET brightness change to be rejected.");
                Assert(server.Send("POST", "/api/displays/rescan").StatusCode == HttpStatusCode.OK, "Expected rescan to succeed.");
            }
        }

        private static MonitorRegistry CreateRegistry(params FakeMonitor[] monitors)
        {
            var registry = new MonitorRegistry(new FakeEnumerator(monitors.Cast<IMonitorDevice>().ToArray()));
            registry.Rescan();
            return registry;
        }

        private static void LoginAcceptsOnlyCorrectToken()
        {
            var auth = new TokenAuthenticator("secret-token");

            Assert(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected wrong token to fail.");
            Assert(auth.TryLogin("").Status == LoginStatus.Failed, "Expected empty token to fail.");
            Assert(auth.TryLogin(null).Status == LoginStatus.Failed, "Expected null token to fail.");
            Assert(auth.TryLogin(" secret-token ").Status == LoginStatus.Succeeded, "Expected correct token to succeed.");
        }

        private static void LoginLocksAfterRepeatedFailures()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var auth = new TokenAuthenticator("secret-token", () => now);

            for (int i = 0; i < 4; i++)
            {
                Assert(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected first four failures not to lock.");
            }
            LoginResult fifth = auth.TryLogin("wrong");
            Assert(fifth.Status == LoginStatus.Locked, "Expected fifth failure to lock.");
            Assert(auth.TryLogin("secret-token").Status == LoginStatus.Locked, "Expected even the correct token to be rejected while locked.");

            now = now.AddSeconds(1);
            Assert(auth.TryLogin("wrong").Status == LoginStatus.Locked, "Expected sixth failure to lock again.");
            LoginResult locked = auth.TryLogin("secret-token");
            Assert(locked.Status == LoginStatus.Locked && locked.RetryAfter == TimeSpan.FromSeconds(2), "Expected lockout to double to 2 seconds.");

            now = now.AddSeconds(2);
            Assert(auth.TryLogin("secret-token").Status == LoginStatus.Succeeded, "Expected login to work after lockout expires.");
        }

        private static void SuccessfulLoginResetsFailureCount()
        {
            var auth = new TokenAuthenticator("secret-token", () => DateTime.UtcNow);

            for (int i = 0; i < 4; i++)
            {
                auth.TryLogin("wrong");
            }
            Assert(auth.TryLogin("secret-token").Status == LoginStatus.Succeeded, "Expected correct token to succeed before lockout.");
            Assert(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected failure count to be reset by success.");
        }

        private static void SessionValueIsBoundToToken()
        {
            var first = new TokenAuthenticator("token-a");
            var same = new TokenAuthenticator("token-a");
            var other = new TokenAuthenticator("token-b");

            Assert(first.IsValidSession(same.SessionValue), "Expected session to survive restart with the same token.");
            Assert(!first.IsValidSession(other.SessionValue), "Expected session from another token to be rejected.");
            Assert(!first.IsValidSession("token-a"), "Expected raw token not to be accepted as a session.");
            Assert(!first.IsValidSession(null) && !first.IsValidSession(""), "Expected empty session to be rejected.");
        }

        private static void TokenIsGeneratedOnceAndReused()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pcmanage-tests-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(dir, "access_token");
            try
            {
                bool created;
                string generated = TokenAuthenticator.LoadOrCreateToken(null, file, out created);
                Assert(created && generated.Length >= 32, "Expected a new random token to be generated.");

                string reused = TokenAuthenticator.LoadOrCreateToken("", file, out created);
                Assert(!created && reused == generated, "Expected the saved token to be reused.");

                string configured = TokenAuthenticator.LoadOrCreateToken(" from-config ", file, out created);
                Assert(!created && configured == "from-config", "Expected configured token to take precedence.");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        private static void WebServerRequiresLogin()
        {
            using (var server = new TestServer())
            {
                HttpWebResponse page = server.Send("GET", "/");
                Assert(page.StatusCode == HttpStatusCode.Redirect && page.Headers["Location"].EndsWith("/login"), "Expected anonymous page request to redirect to /login.");
                Assert(server.Send("GET", "/login").StatusCode == HttpStatusCode.OK, "Expected login page to be public.");
                Assert(server.Send("GET", "/Home/GetShutdownTimingVal").StatusCode == HttpStatusCode.Unauthorized, "Expected anonymous API request to be rejected.");
                Assert(server.Send("POST", "/api/login", "token=wrong").StatusCode == HttpStatusCode.Unauthorized, "Expected wrong token to be rejected.");
                Assert(server.Send("GET", "/api/login").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET login to be rejected.");

                HttpWebResponse login = server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));
                string setCookie = login.Headers["Set-Cookie"] ?? string.Empty;
                Assert(login.StatusCode == HttpStatusCode.OK, "Expected correct token to log in.");
                Assert(setCookie.Contains("HttpOnly") && setCookie.Contains("SameSite=Strict"), "Expected a hardened session cookie.");

                Assert(server.Send("GET", "/").StatusCode == HttpStatusCode.OK, "Expected page to load after login.");
                Assert(server.Send("GET", "/Home/GetShutdownTimingVal").StatusCode == HttpStatusCode.OK, "Expected API to work after login.");
            }
        }

        private static void WebServerAcceptsTokenHeader()
        {
            using (var server = new TestServer())
            {
                var headers = new Dictionary<string, string> { { TokenAuthenticator.TokenHeaderName, TestServer.Token } };
                Assert(server.Send("GET", "/Home/GetShutdownTimingVal", null, headers).StatusCode == HttpStatusCode.OK, "Expected token header to authenticate.");
                headers[TokenAuthenticator.TokenHeaderName] = "wrong";
                Assert(server.Send("GET", "/Home/GetShutdownTimingVal", null, headers).StatusCode == HttpStatusCode.Unauthorized, "Expected wrong token header to be rejected.");
            }
        }

        private static void RequestGuardRequiresHeaderAndSameOrigin()
        {
            Assert(!RequestGuard.IsTrustedPost(null, null, "pc:9527", null), "Expected missing CSRF header to be rejected.");
            Assert(!RequestGuard.IsTrustedPost(null, "0", "pc:9527", null), "Expected wrong CSRF header value to be rejected.");
            Assert(RequestGuard.IsTrustedPost(null, "1", "pc:9527", null), "Expected header without Origin to pass.");
            Assert(RequestGuard.IsTrustedPost("http://PC:9527", "1", "pc:9527", null), "Expected same origin to pass.");
            Assert(RequestGuard.IsTrustedPost("http://pc", "1", "pc:80", null), "Expected default port to match.");
            Assert(!RequestGuard.IsTrustedPost("http://evil.example", "1", "pc:9527", null), "Expected foreign origin to be rejected.");
            Assert(!RequestGuard.IsTrustedPost("http://pc:9999", "1", "pc:9527", null), "Expected different port to be rejected.");
            Assert(!RequestGuard.IsTrustedPost("null", "1", "pc:9527", null), "Expected opaque origin to be rejected.");
            Assert(RequestGuard.IsTrustedPost("https://pc.example.com", "1", "127.0.0.1:9527", "pc.example.com"), "Expected X-Forwarded-Host to be accepted.");
        }

        private static void WebServerRejectsUnsafeStateChanges()
        {
            using (var server = new TestServer())
            {
                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));

                // 只用 seconds=0（取消定时关机）验证，不会真的触发关机。
                Assert(server.Send("GET", "/Home/TimingShutdown?seconds=0").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET state change to be rejected.");
                Assert(server.Send("GET", "/Home/LockWindows").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET lock to be rejected.");
                Assert(server.Send("POST", "/Home/TimingShutdown", "seconds=0", null, false).StatusCode == HttpStatusCode.Forbidden, "Expected POST without CSRF header to be rejected.");
                Assert(server.Send("POST", "/api/logout", null, null, false).StatusCode == HttpStatusCode.Forbidden, "Expected logout without CSRF header to be rejected.");

                var foreign = new Dictionary<string, string> { { "Origin", "http://evil.example" } };
                Assert(server.Send("POST", "/Home/TimingShutdown", "seconds=0", foreign).StatusCode == HttpStatusCode.Forbidden, "Expected cross-origin POST to be rejected.");

                Assert(server.Send("POST", "/Home/TimingShutdown", "seconds=0").StatusCode == HttpStatusCode.OK, "Expected trusted POST to succeed.");
                Assert(server.Send("POST", "/api/logout").StatusCode == HttpStatusCode.OK, "Expected logout to succeed.");
                Assert(server.Send("GET", "/Home/GetShutdownTimingVal").StatusCode == HttpStatusCode.Unauthorized, "Expected session to be cleared by logout.");
            }
        }

        private sealed class TestServer : IDisposable
        {
            public const string Token = "integration-token";

            private readonly LocalWebServer _server;
            private readonly MonitorRegistry _registry;
            private readonly CookieContainer _cookies = new CookieContainer();
            private readonly int _port;

            public string LastBody { get; private set; }

            public TestServer(MonitorRegistry registry = null)
            {
                _registry = registry ?? CreateRegistry(new FakeMonitor("aaaa0001", "Test Panel"));

                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                _server = new LocalWebServer(
                    _registry,
                    new AudioService(),
                    new WindowsManage(),
                    new TokenAuthenticator(Token),
                    _port,
                    "127.0.0.1",
                    log4net.LogManager.GetLogger("Tests"));
                Assert(_server.Start(), "Expected test web server to start.");
            }

            public HttpWebResponse Send(string method, string path, string form = null, IDictionary<string, string> headers = null, bool csrfHeader = true)
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
                request.Method = method;
                request.AllowAutoRedirect = false;
                request.CookieContainer = _cookies;
                if (method == "POST" && csrfHeader)
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
                else if (method == "POST")
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
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FakeMonitor : IMonitorDevice
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
        private sealed class FakeEnumerator : IMonitorEnumerator
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
