using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PCDeviceManage.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                Test(nameof(ForcedReadRefreshesDisplayList), ForcedReadRefreshesDisplayList),
                Test(nameof(CachedReadCanReturnLastDisplayList), CachedReadCanReturnLastDisplayList),
                Test(nameof(DisplayManageReadRefreshesDisplayList), DisplayManageReadRefreshesDisplayList),
                Test(nameof(LoginAcceptsOnlyCorrectToken), LoginAcceptsOnlyCorrectToken),
                Test(nameof(LoginLocksAfterRepeatedFailures), LoginLocksAfterRepeatedFailures),
                Test(nameof(SuccessfulLoginResetsFailureCount), SuccessfulLoginResetsFailureCount),
                Test(nameof(SessionValueIsBoundToToken), SessionValueIsBoundToToken),
                Test(nameof(TokenIsGeneratedOnceAndReused), TokenIsGeneratedOnceAndReused),
                Test(nameof(WebServerRequiresLogin), WebServerRequiresLogin),
                Test(nameof(WebServerAcceptsTokenHeader), WebServerAcceptsTokenHeader),
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

        private static void ForcedReadRefreshesDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            provider.SetBrightness(40);
            var cache = new DisplayDeviceCache(provider);

            string first = cache.GetDisplayListJson(true);
            provider.SetBrightness(80);
            string second = cache.GetDisplayListJson(true);

            Assert(provider.CallCount == 2, "Expected forced reads to call provider each time.");
            Assert(first.Contains("\"Brightness\":40"), "Expected first brightness to be serialized.");
            Assert(second.Contains("\"Brightness\":80"), "Expected second brightness to be refreshed.");
        }

        private static void CachedReadCanReturnLastDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            var cache = new DisplayDeviceCache(provider);

            provider.SetBrightness(25);
            cache.GetDisplayListJson(true);
            provider.SetBrightness(75);
            string cached = cache.GetDisplayListJson(false);

            Assert(provider.CallCount == 1, "Expected cached read to avoid provider call.");
            Assert(cached.Contains("\"Brightness\":25"), "Expected cached brightness to remain available.");
            Assert(!cached.Contains("\"Brightness\":75"), "Expected cached read not to refresh state.");
        }

        private static void DisplayManageReadRefreshesDisplayList()
        {
            var provider = new FakeDisplayDeviceProvider();
            provider.SetBrightness(10);
            var cache = new DisplayDeviceCache(provider);
            var manager = new DisplayManage(cache);

            provider.SetBrightness(90);
            string refreshed = manager.GetDisplayList();

            Assert(provider.CallCount == 2, "Expected DisplayManage.GetDisplayList to refresh provider state.");
            Assert(refreshed.Contains("\"Brightness\":90"), "Expected DisplayManage.GetDisplayList to return refreshed brightness.");
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

        private sealed class TestServer : IDisposable
        {
            public const string Token = "integration-token";

            private readonly LocalWebServer _server;
            private readonly CookieContainer _cookies = new CookieContainer();
            private readonly int _port;

            public TestServer()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                _server = new LocalWebServer(
                    new DisplayManage(new DisplayDeviceCache(new FakeDisplayDeviceProvider())),
                    new AudioManage(),
                    new WindowsManage(),
                    new TokenAuthenticator(Token),
                    _port,
                    "127.0.0.1",
                    log4net.LogManager.GetLogger("Tests"));
                Assert(_server.Start(), "Expected test web server to start.");
            }

            public HttpWebResponse Send(string method, string path, string form = null, IDictionary<string, string> headers = null)
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
                request.Method = method;
                request.AllowAutoRedirect = false;
                request.CookieContainer = _cookies;
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

                try
                {
                    var response = (HttpWebResponse)request.GetResponse();
                    response.Close();
                    return response;
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    var response = (HttpWebResponse)ex.Response;
                    response.Close();
                    return response;
                }
            }

            public void Dispose()
            {
                _server.Dispose();
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FakeDisplayDeviceProvider : IDisplayDeviceProvider
        {
            private int _brightness;

            public int CallCount { get; private set; }

            public void SetBrightness(int brightness)
            {
                _brightness = brightness;
            }

            public List<DisplayInfoGet.DeviceItemPlus> GetDisplayListAndBrightness()
            {
                CallCount++;
                return new List<DisplayInfoGet.DeviceItemPlus>
                {
                    new DisplayInfoGet.DeviceItemPlus(
                        new DisplayContext.DeviceItem("DISPLAY\\TEST", "Test Panel", 1, 0),
                        "Test Panel",
                        true)
                    {
                        Brightness = _brightness
                    }
                };
            }
        }
    }
}
