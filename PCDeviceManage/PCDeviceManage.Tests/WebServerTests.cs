using Microsoft.Extensions.DependencyInjection;
using PCDeviceManage.App;
using PCDeviceManage.Devices.Audio;
using PCDeviceManage.Devices.Power;
using PCDeviceManage.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static PCDeviceManage.Tests.TestSupport;

namespace PCDeviceManage.Tests
{
    public class WebServerTests
    {
        [Fact]
        public void WebHostSettingMapsToListenAddress()
        {
            Assert.Equal(IPAddress.IPv6Any, WebConsole.ResolveAddress("+"));
            Assert.Equal(IPAddress.IPv6Any, WebConsole.ResolveAddress("0.0.0.0"));
            Assert.Equal(IPAddress.Loopback, WebConsole.ResolveAddress("localhost"));
            Assert.Equal(IPAddress.Parse("192.168.1.5"), WebConsole.ResolveAddress("192.168.1.5"));
            Assert.Throws<ArgumentException>(() => WebConsole.ResolveAddress("my-pc"));
        }

        [Fact]
        public async Task HostRunsLifetimeHooksEvenWhenPortIsBusy()
        {
            var busy = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            busy.Start();
            try
            {
                int port = ((IPEndPoint)busy.LocalEndpoint).Port;
                int started = 0;
                int stopped = 0;
                var scheduler = new ShutdownScheduler(() => { });
                var app = AppHost.Build("127.0.0.1", port, services =>
                {
                    services.AddSingleton(CreateRegistry());
                    services.AddSingleton(new AudioService());
                    services.AddSingleton(new PowerService());
                    services.AddSingleton(scheduler);
                    services.AddSingleton(new TokenAuthenticator("token"));
                    services.AddLifetimeHook(provider => started++, provider => stopped++);
                });

                await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync());
                Assert.Equal(1, started);

                await app.StopAsync();
                await app.DisposeAsync();
                scheduler.Dispose();
                Assert.Equal(1, stopped);
            }
            finally
            {
                busy.Stop();
            }
        }

        [Fact]
        public void WebServerRequiresLogin()
        {
            using (var server = new TestServer())
            {
                TestResponse page = server.Send("GET", "/");
                Assert.True(page.StatusCode == HttpStatusCode.Redirect && page.Headers["Location"].EndsWith("/login"), "Expected anonymous page request to redirect to /login.");
                Assert.True(server.Send("GET", "/login").StatusCode == HttpStatusCode.OK, "Expected login page to be public.");
                Assert.True(server.Send("GET", "/api/power/schedule").StatusCode == HttpStatusCode.Unauthorized, "Expected anonymous API request to be rejected.");
                Assert.True(server.Send("POST", "/api/login", "token=wrong").StatusCode == HttpStatusCode.Unauthorized, "Expected wrong token to be rejected.");
                Assert.True(server.Send("GET", "/api/login").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET login to be rejected.");

                TestResponse login = server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));
                string setCookie = login.Headers["Set-Cookie"] ?? string.Empty;
                Assert.True(login.StatusCode == HttpStatusCode.OK, "Expected correct token to log in.");
                Assert.True(setCookie.IndexOf("httponly", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    setCookie.IndexOf("samesite=strict", StringComparison.OrdinalIgnoreCase) >= 0, "Expected a hardened session cookie, got " + setCookie);

                Assert.True(server.Send("GET", "/").StatusCode == HttpStatusCode.OK, "Expected page to load after login.");
                Assert.True(server.Send("GET", "/api/power/schedule").StatusCode == HttpStatusCode.OK, "Expected API to work after login.");
            }
        }

        [Fact]
        public void WebServerAcceptsTokenHeader()
        {
            using (var server = new TestServer())
            {
                var headers = new Dictionary<string, string> { { TokenAuthenticator.TokenHeaderName, TestServer.Token } };
                Assert.True(server.Send("GET", "/api/power/schedule", null, headers).StatusCode == HttpStatusCode.OK, "Expected token header to authenticate.");
                headers[TokenAuthenticator.TokenHeaderName] = "wrong";
                Assert.True(server.Send("GET", "/api/power/schedule", null, headers).StatusCode == HttpStatusCode.Unauthorized, "Expected wrong token header to be rejected.");
            }
        }

        [Fact]
        public void WebServerRejectsUnsafeStateChanges()
        {
            using (var server = new TestServer())
            {
                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));

                // 只用取消定时关机（seconds=0 / DELETE）验证写操作，不会真的锁屏或关机。
                Assert.True(server.Send("GET", "/api/power/lock").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET lock to be rejected.");
                Assert.True(server.Send("GET", "/api/power/shutdown").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET shutdown to be rejected.");
                Assert.True(server.Send("POST", "/api/power/schedule", "seconds=0", null, false).StatusCode == HttpStatusCode.Forbidden, "Expected POST without CSRF header to be rejected.");
                Assert.True(server.Send("DELETE", "/api/power/schedule", null, null, false).StatusCode == HttpStatusCode.Forbidden, "Expected DELETE without CSRF header to be rejected.");
                Assert.True(server.Send("POST", "/api/logout", null, null, false).StatusCode == HttpStatusCode.Forbidden, "Expected logout without CSRF header to be rejected.");

                var foreign = new Dictionary<string, string> { { "Origin", "http://evil.example" } };
                Assert.True(server.Send("POST", "/api/power/schedule", "seconds=0", foreign).StatusCode == HttpStatusCode.Forbidden, "Expected cross-origin POST to be rejected.");

                Assert.True(server.Send("POST", "/api/power/schedule", "seconds=0").StatusCode == HttpStatusCode.OK, "Expected trusted POST to succeed.");
                Assert.True(server.Send("POST", "/api/logout").StatusCode == HttpStatusCode.OK, "Expected logout to succeed.");
                Assert.True(server.Send("GET", "/api/power/schedule").StatusCode == HttpStatusCode.Unauthorized, "Expected session to be cleared by logout.");
            }
        }

        [Fact]
        public void WebServerServesStaticFilesAndErrors()
        {
            using (var server = new TestServer())
            {
                Assert.True(server.Send("GET", "/favicon.ico").StatusCode == HttpStatusCode.OK, "Expected the favicon to be served.");
                Assert.True(server.Send("GET", "/login").StatusCode == HttpStatusCode.OK && server.LastBody.Contains("/api/login"), "Expected the embedded login page.");
                Assert.True(server.Send("GET", "/api/displays").StatusCode == HttpStatusCode.Unauthorized && server.LastBody.Contains("\"ok\":false"), "Expected a JSON 401 for anonymous API calls.");

                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));
                Assert.True(server.LastBody == "{\"ok\":true,\"data\":null}", "Expected the success envelope, got " + server.LastBody);
                Assert.True(server.Send("GET", "/").StatusCode == HttpStatusCode.OK && server.LastBody.Contains("/api/power/schedule"), "Expected the embedded console page.");
                Assert.True(server.Send("GET", "/home").StatusCode == HttpStatusCode.OK, "Expected the old /home address to still open the console.");
                Assert.True(server.Send("GET", "/api/nothing").StatusCode == HttpStatusCode.NotFound && server.LastBody.Contains("\"ok\":false"), "Expected a JSON 404 for unknown APIs.");

                TestResponse wrongMethod = server.Send("PUT", "/api/power/schedule");
                Assert.True(wrongMethod.StatusCode == HttpStatusCode.MethodNotAllowed, "Expected 405 for an unmapped method.");
                Assert.True(wrongMethod.Headers["Allow"].Contains("DELETE") && wrongMethod.Headers["Allow"].Contains("GET"), "Expected the Allow header to list mapped methods.");
            }
        }

        [Fact]
        public void WebServerSchedulesShutdown()
        {
            using (var server = new TestServer())
            {
                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));
                Assert.True(server.Send("POST", "/api/power/schedule", "seconds=600").StatusCode == HttpStatusCode.OK, "Expected schedule to succeed.");
                Assert.True(server.Scheduler.Deadline.HasValue, "Expected scheduler to hold a deadline.");

                server.Send("GET", "/api/power/schedule");
                using (var state = System.Text.Json.JsonDocument.Parse(server.LastBody))
                {
                    var data = state.RootElement.GetProperty("data");
                    int remaining = data.GetProperty("remainingSeconds").GetInt32();
                    Assert.True(state.RootElement.GetProperty("ok").GetBoolean() && remaining > 590 && remaining <= 600, "Expected about 600 seconds remaining, got " + server.LastBody);
                    Assert.True(data.GetProperty("deadline").GetDateTime() > DateTime.UtcNow, "Expected the deadline to be reported.");
                }

                Assert.True(server.Send("POST", "/api/power/schedule", "seconds=abc").StatusCode == HttpStatusCode.BadRequest, "Expected invalid seconds to return 400.");
                Assert.True(server.LastBody.Contains("\"ok\":false"), "Expected an error envelope.");

                Assert.True(server.Send("DELETE", "/api/power/schedule").StatusCode == HttpStatusCode.OK, "Expected cancel to succeed.");
                Assert.True(!server.Scheduler.Deadline.HasValue && server.ShutdownCalls == 0, "Expected schedule to be cancelled without shutting down.");
            }
        }

        [Fact]
        public void WebServerSetsBrightnessById()
        {
            var a = new FakeMonitor("aaaa0001", "External A");
            var b = new FakeMonitor("bbbb0002", "External B");
            using (var server = new TestServer(CreateRegistry(a, b)))
            {
                server.Send("POST", "/api/login", "token=" + Uri.EscapeDataString(TestServer.Token));

                Assert.True(server.Send("GET", "/api/displays").StatusCode == HttpStatusCode.OK, "Expected display listing to succeed.");
                Assert.True(server.LastBody.Contains("\"id\":\"bbbb0002\"") && server.LastBody.Contains("\"supported\":true"), "Expected listing JSON with ids, got " + server.LastBody);

                Assert.True(server.Send("POST", "/api/displays/bbbb0002/brightness", "value=45").StatusCode == HttpStatusCode.OK, "Expected brightness write to succeed.");
                Assert.True(b.Writes.SequenceEqual(new[] { 45 }) && a.Writes.Count == 0, "Expected only display B to change.");

                Assert.True(server.Send("POST", "/api/displays/ffffffff/brightness", "value=45").StatusCode == HttpStatusCode.NotFound, "Expected unknown display to return 404.");
                Assert.True(server.Send("POST", "/api/displays/bbbb0002/brightness", "value=101").StatusCode == HttpStatusCode.BadRequest, "Expected out-of-range value to return 400.");
                Assert.True(server.Send("GET", "/api/displays/bbbb0002/brightness").StatusCode == HttpStatusCode.MethodNotAllowed, "Expected GET brightness change to be rejected.");
                Assert.True(server.Send("POST", "/api/displays/rescan").StatusCode == HttpStatusCode.OK, "Expected rescan to succeed.");
            }
        }
    }
}
