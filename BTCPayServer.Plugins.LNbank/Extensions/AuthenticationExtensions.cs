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
                .AddScheme<AuthenticationSchemeOptions, BTCPayAPIKeyAuthenticationHandler>(AuthenticationSchemes.ApiBTCPayAPIKey, o => {})
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(AuthenticationSchemes.ApiBasic, o => {})
                .AddCookie(options =>
                {
                    // Forward API and Hub requests to API key scheme
                    options.ForwardDefaultSelector = ctx =>
                    {
                        string authHeader = ctx.Request.Headers["Authorization"];
                        bool isBearerAuth = authHeader != null && authHeader.StartsWith("Bearer ");
                        bool isApiOrHub = ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/Hubs");

                        return isApiOrHub && isBearerAuth
                            ? AuthenticationSchemes.ApiBTCPayAPIKey
                            : null;
                    };

                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Logout";
                    options.Cookie.Name = "LNbank";
                });
        }
    }
}
