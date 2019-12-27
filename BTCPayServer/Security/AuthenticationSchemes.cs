﻿using OpenIddict.Validation.AspNetCore;

namespace BTCPayServer.Security
{
    public class AuthenticationSchemes
    {
        public const string Cookie = "Identity.Application";
        public const string Bitpay = "Bitpay";
        public const string OpenId = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    }
}
