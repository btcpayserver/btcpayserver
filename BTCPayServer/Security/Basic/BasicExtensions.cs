using BTCPayServer.Security.Basic;
using Microsoft.AspNetCore.Authentication;

namespace BTCPayServer.Security.APIKeys
{
    public static class BasicExtensions
    {

        public static AuthenticationBuilder AddBasicAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(AuthenticationSchemes.Basic,
                o => { });
            return builder;
        }
        
    }
}
