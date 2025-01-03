using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Security.GreenField;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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

        public static IServiceCollection AddBearerAuthentication(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IAuthorizationHandler, BearerAuthorizationHandler>();
            return serviceCollection;
        }
    }
}
