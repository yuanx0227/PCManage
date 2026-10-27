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
    public class AuthTests
    {
        [Fact]
        public void LoginAcceptsOnlyCorrectToken()
        {
            var auth = new TokenAuthenticator("secret-token");

            Assert.True(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected wrong token to fail.");
            Assert.True(auth.TryLogin("").Status == LoginStatus.Failed, "Expected empty token to fail.");
            Assert.True(auth.TryLogin(null).Status == LoginStatus.Failed, "Expected null token to fail.");
            Assert.True(auth.TryLogin(" secret-token ").Status == LoginStatus.Succeeded, "Expected correct token to succeed.");
        }

        [Fact]
        public void LoginLocksAfterRepeatedFailures()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var auth = new TokenAuthenticator("secret-token", () => now);

            for (int i = 0; i < 4; i++)
            {
                Assert.True(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected first four failures not to lock.");
            }
            LoginResult fifth = auth.TryLogin("wrong");
            Assert.True(fifth.Status == LoginStatus.Locked, "Expected fifth failure to lock.");
            Assert.True(auth.TryLogin("secret-token").Status == LoginStatus.Locked, "Expected even the correct token to be rejected while locked.");

            now = now.AddSeconds(1);
            Assert.True(auth.TryLogin("wrong").Status == LoginStatus.Locked, "Expected sixth failure to lock again.");
            LoginResult locked = auth.TryLogin("secret-token");
            Assert.True(locked.Status == LoginStatus.Locked && locked.RetryAfter == TimeSpan.FromSeconds(2), "Expected lockout to double to 2 seconds.");

            now = now.AddSeconds(2);
            Assert.True(auth.TryLogin("secret-token").Status == LoginStatus.Succeeded, "Expected login to work after lockout expires.");
        }

        [Fact]
        public void SuccessfulLoginResetsFailureCount()
        {
            var auth = new TokenAuthenticator("secret-token", () => DateTime.UtcNow);

            for (int i = 0; i < 4; i++)
            {
                auth.TryLogin("wrong");
            }
            Assert.True(auth.TryLogin("secret-token").Status == LoginStatus.Succeeded, "Expected correct token to succeed before lockout.");
            Assert.True(auth.TryLogin("wrong").Status == LoginStatus.Failed, "Expected failure count to be reset by success.");
        }

        [Fact]
        public void SessionValueIsBoundToToken()
        {
            var first = new TokenAuthenticator("token-a");
            var same = new TokenAuthenticator("token-a");
            var other = new TokenAuthenticator("token-b");

            Assert.True(first.IsValidSession(same.SessionValue), "Expected session to survive restart with the same token.");
            Assert.True(!first.IsValidSession(other.SessionValue), "Expected session from another token to be rejected.");
            Assert.True(!first.IsValidSession("token-a"), "Expected raw token not to be accepted as a session.");
            Assert.True(!first.IsValidSession(null) && !first.IsValidSession(""), "Expected empty session to be rejected.");
        }

        [Fact]
        public void RequestGuardRequiresHeaderAndSameOrigin()
        {
            Assert.True(!RequestGuard.IsTrustedPost(null, null, "pc:9527", null), "Expected missing CSRF header to be rejected.");
            Assert.True(!RequestGuard.IsTrustedPost(null, "0", "pc:9527", null), "Expected wrong CSRF header value to be rejected.");
            Assert.True(RequestGuard.IsTrustedPost(null, "1", "pc:9527", null), "Expected header without Origin to pass.");
            Assert.True(RequestGuard.IsTrustedPost("http://PC:9527", "1", "pc:9527", null), "Expected same origin to pass.");
            Assert.True(RequestGuard.IsTrustedPost("http://pc", "1", "pc:80", null), "Expected default port to match.");
            Assert.True(!RequestGuard.IsTrustedPost("http://evil.example", "1", "pc:9527", null), "Expected foreign origin to be rejected.");
            Assert.True(!RequestGuard.IsTrustedPost("http://pc:9999", "1", "pc:9527", null), "Expected different port to be rejected.");
            Assert.True(!RequestGuard.IsTrustedPost("null", "1", "pc:9527", null), "Expected opaque origin to be rejected.");
            Assert.True(RequestGuard.IsTrustedPost("https://pc.example.com", "1", "127.0.0.1:9527", "pc.example.com"), "Expected X-Forwarded-Host to be accepted.");
        }
    }
}
