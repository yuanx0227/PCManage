using System;

namespace PCDeviceManage
{
    /// <summary>
    /// 防跨站请求伪造：会改变状态的 POST 必须带自定义请求头（跨站请求带自定义头会触发 CORS 预检，而本服务不响应预检），
    /// 浏览器带了 Origin 时还必须与 Host 同源。
    /// </summary>
    public static class RequestGuard
    {
        public const string CsrfHeaderName = "X-PCM-Request";

        public static bool IsTrustedPost(string origin, string csrfHeader, string host, string forwardedHost)
        {
            if (csrfHeader != "1")
            {
                return false;
            }

            if (string.IsNullOrEmpty(origin))
            {
                return true;
            }

            Uri originUri;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out originUri))
            {
                return false;
            }

            return SameAuthority(originUri, host) || SameAuthority(originUri, forwardedHost);
        }

        private static bool SameAuthority(Uri origin, string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            Uri hostUri;
            if (!Uri.TryCreate(origin.Scheme + "://" + host.Trim(), UriKind.Absolute, out hostUri))
            {
                return false;
            }

            return string.Equals(origin.Host, hostUri.Host, StringComparison.OrdinalIgnoreCase) && origin.Port == hostUri.Port;
        }
    }
}
