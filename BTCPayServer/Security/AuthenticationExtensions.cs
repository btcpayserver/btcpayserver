using System;
using BTCPayApp.CommonServer;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Security
{
    public static class AuthenticationExtensions
    {
        public static AuthenticationBuilder AddBitpayAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<BitpayAuthenticationOptions, BitpayAuthenticationHandler>(AuthenticationSchemes.Bitpay, o => { });
            return builder;
        }

        public static AuthenticationBuilder AddBearerAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddBearerToken(AuthenticationSchemes.GreenfieldBearer, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(30.0);
                options.RefreshTokenExpiration = TimeSpan.FromDays(3.0);
            });
            return builder;
        }
    }
}
