using BTCPayServer.Plugins.LNbank.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.LNbank.Extensions
{
    public static class AuthenticationExtensions
    {
        public static void AddAppAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, BTCPayAPIKeyAuthenticationHandler>(AuthenticationSchemes.Api, o => {})
                .AddCookie(options =>
                {
                    // Forward API and Hub requests to API key scheme
                    options.ForwardDefaultSelector = ctx =>
                    {
                        string authHeader = ctx.Request.Headers["Authorization"];
                        bool isBearerAuth = authHeader != null && authHeader.StartsWith("Bearer ");
                        bool isApiOrHub = ctx.Request.Path.StartsWithSegments("/Plugins/LNbank/api") || ctx.Request.Path.StartsWithSegments("/Plugins/LNbank/Hubs");

                        return isApiOrHub && isBearerAuth
                            ? AuthenticationSchemes.Api
                            : null;
                    };

                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.Cookie.Name = "LNbank";
                });
        }
    }
}
