using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using BTCPayServer.Configuration;

namespace BTCPayServer.Hosting
{
    public class HeadersOverrideMiddleware
    {
        RequestDelegate _Next;
        string overrideXForwardedProto;
        public HeadersOverrideMiddleware(RequestDelegate next,
            IConfiguration options)
        {
            _Next = next ?? throw new ArgumentNullException(nameof(next));
            overrideXForwardedProto = options.GetOrDefault<string>("xforwardedproto", null);
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (!string.IsNullOrEmpty(overrideXForwardedProto))
            {
                if (!httpContext.Request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
                {
                    httpContext.Request.Headers["X-Forwarded-Proto"] = overrideXForwardedProto;
                }
            }
            await _Next(httpContext);
        }
    }
}
