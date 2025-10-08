using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions;

public record RequestBaseUrl(string Scheme, HostString Host, PathString PathBase)
{
    public static RequestBaseUrl FromUrl(Uri url)
    => new RequestBaseUrl(url.Scheme, new HostString(url.Authority), new PathString(url.AbsolutePath));

    public static RequestBaseUrl FromUrl(string url)
    {
        if (TryFromUrl(url, out var result))
            return result;
        throw new FormatException("Invalid RequestBaseUrl");
    }
    public static bool TryFromUrl(string url, [MaybeNullWhen(false)] out RequestBaseUrl result)
    {
        ArgumentNullException.ThrowIfNull(url);
        result = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var o))
            return false;
        result = FromUrl(o);
        return true;
    }

    public RequestBaseUrl(HttpRequest request) : this(request.Scheme, request.Host, request.PathBase)
    {

    }

    public override string ToString()
    => string.Concat(
        Scheme,
        "://",
        Host.ToUriComponent(),
        PathBase.ToUriComponent());
}
