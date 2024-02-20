using System;
using BTCPayServer.Abstractions.Constants;
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
            builder.AddBearerToken(AuthenticationSchemes.Bearer, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(30.0);
                options.RefreshTokenExpiration = TimeSpan.FromDays(3.0);
                // Customize token handling, https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/BearerToken/src/BearerTokenHandler.cs
                /*options.Events.OnMessageReceived = context =>
                {
                    /*context.Fail("Failure Message");#1#
                    const string start = "Bearer ";
                    var auth = context.Request.Headers.Authorization.ToString();
                    if (auth.StartsWith(start, StringComparison.InvariantCultureIgnoreCase))
                    {
                        context.Token = auth[start.Length..];
                    }
                    return Task.CompletedTask;
                };*/
            });
            return builder;
        }
    }
}
