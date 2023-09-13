using LNURL;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins;

public class LightningAddressResolver
{
    public string Username { get; set; }
    public HttpContext HttpContext { get; }
    public LNURLPayRequest LNURLPayRequest { get; private set; }

    public LightningAddressResolver(HttpContext httpContext, string username)
    {
        HttpContext = httpContext;
        Username = username;
    }

    public void ResolveLNURLPayRequest(LNURLPayRequest lnurlPayRequest)
    {
        LNURLPayRequest = lnurlPayRequest;
    }
}
