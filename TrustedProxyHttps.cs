using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

public static class TrustedProxyHttps
{
    public static ForwardedHeadersOptions Options()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
        return options;
    }

    public static string CallbackUrl(string applicationBaseUrl)
    {
        if (!Uri.TryCreate(applicationBaseUrl, UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
            (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            throw new ArgumentException("Configure ASTRA's public address as an HTTPS origin.");
        return new Uri(origin, "/api/auth/callback").AbsoluteUri;
    }
}
