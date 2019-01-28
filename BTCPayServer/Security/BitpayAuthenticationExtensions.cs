using System;
using Microsoft.AspNetCore.Authentication;

namespace BTCPayServer.Security
{
    public static class BitpayAuthenticationExtensions
    {
        public static AuthenticationBuilder AddBitpayAuthentication(this AuthenticationBuilder builder,
            Action<BitpayAuthentication.BitpayAuthOptions> bitpayAuth = null)
        {
            BitpayAuthentication.AddAuthentication(builder,bitpayAuth);
            return builder;
        }
    }
} 
