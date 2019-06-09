using System;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authentication;

namespace BTCPayServer.Security
{
    public static class AuthenticationExtensions
    {
        public static AuthenticationBuilder AddBitpayAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<BitpayAuthenticationOptions, BitpayAuthenticationHandler>(Policies.BitpayAuthentication, o => { });
            return builder;
        }
    }
} 
