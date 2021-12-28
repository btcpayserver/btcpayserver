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
            var builder = new AuthenticationBuilder(services);
            builder.AddScheme<LnBankAuthenticationOptions, LnBankAuthenticationHandler>(AuthenticationSchemes.Api,
                    o => { });
        }
    }
}
