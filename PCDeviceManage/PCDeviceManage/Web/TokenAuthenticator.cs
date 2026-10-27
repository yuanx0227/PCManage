using System;
using System.Security.Cryptography;
using System.Text;

namespace PCDeviceManage.Web
{
    public enum LoginStatus
    {
        Succeeded,
        Failed,
        Locked
    }

    public sealed class LoginResult
    {
        public LoginStatus Status { get; }
        public TimeSpan RetryAfter { get; }

        public LoginResult(LoginStatus status, TimeSpan retryAfter)
        {
            Status = status;
            RetryAfter = retryAfter;
        }
    }

    /// <summary>
    /// 访问令牌校验。失败次数按全局统计而不是按 IP：经 frp 转发后所有请求的来源都是本机 frpc。
    /// </summary>
    public sealed class TokenAuthenticator
    {
        public const string SessionCookieName = "pcm_session";
        public const string TokenHeaderName = "X-Access-Token";

        private const int FreeAttempts = 5;
        private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

        private readonly byte[] _tokenHash;
        private readonly string _sessionValue;
        private readonly Func<DateTime> _clock;
        private readonly object _locker = new object();
        private int _consecutiveFailures;
        private DateTime _lockedUntil = DateTime.MinValue;

        public TokenAuthenticator(string token, Func<DateTime> clock = null)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("Access token must not be empty.", nameof(token));
            }

            _tokenHash = Sha256(token.Trim());
            _sessionValue = ComputeSessionValue(token.Trim());
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// 会话 Cookie 的值：由令牌派生，程序重启后仍然有效，更换令牌后自动失效。
        /// </summary>
        public string SessionValue => _sessionValue;

        public bool IsValidSession(string cookieValue)
        {
            if (string.IsNullOrEmpty(cookieValue))
            {
                return false;
            }

            return FixedTimeEquals(Sha256(cookieValue), Sha256(_sessionValue));
        }

        public LoginResult TryLogin(string candidate)
        {
            lock (_locker)
            {
                DateTime now = _clock();
                if (now < _lockedUntil)
                {
                    return new LoginResult(LoginStatus.Locked, _lockedUntil - now);
                }

                if (!string.IsNullOrEmpty(candidate) && FixedTimeEquals(Sha256(candidate.Trim()), _tokenHash))
                {
                    _consecutiveFailures = 0;
                    _lockedUntil = DateTime.MinValue;
                    return new LoginResult(LoginStatus.Succeeded, TimeSpan.Zero);
                }

                _consecutiveFailures++;
                if (_consecutiveFailures < FreeAttempts)
                {
                    return new LoginResult(LoginStatus.Failed, TimeSpan.Zero);
                }

                // 第 5 次失败起锁定 1s、2s、4s……，最长 15 分钟。
                int exponent = Math.Min(_consecutiveFailures - FreeAttempts, 20);
                TimeSpan lockout = TimeSpan.FromSeconds(Math.Pow(2, exponent));
                if (lockout > MaxLockout)
                {
                    lockout = MaxLockout;
                }
                _lockedUntil = now + lockout;
                return new LoginResult(LoginStatus.Locked, lockout);
            }
        }

        private static string ComputeSessionValue(string token)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token)))
            {
                return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes("pcmanage-session-v1")));
            }
        }

        private static byte[] Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }
            return diff == 0;
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
