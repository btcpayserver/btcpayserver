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
    }
}
