using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions;

public record RequestBaseUrl(string Scheme, HostString Host, PathString PathBase)
{
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
