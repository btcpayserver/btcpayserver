using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace BTCPayServer.Hosting;

public class OnionLocationMiddleware(RequestDelegate next, BTCPayServerEnvironment env)
{
    public Task Invoke(HttpContext httpContext)
    {
        var isHtml = httpContext.Request.Headers.TryGetValue("Accept", out var accept)
                     && accept.ToString().StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        var isModal = httpContext.Request.Query.TryGetValue("view", out var view)
                      && view.ToString().Equals("modal", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(env.OnionUrl) &&
            !httpContext.Request.IsOnion() &&
            isHtml &&
            !isModal)
        {
            var onionLocation = env.OnionUrl + httpContext.Request.GetEncodedPathAndQuery();
            httpContext.Response.SetHeader("Onion-Location", onionLocation);
        }
        return next(httpContext);
    }

}
