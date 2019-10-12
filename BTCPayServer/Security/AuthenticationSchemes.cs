using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenIddict.Validation;

namespace BTCPayServer.Security
{
    public class AuthenticationSchemes
    {
        public const string Cookie = "Identity.Application";
        public const string Bitpay = "Bitpay";
        public const string OpenId = OpenIddictValidationDefaults.AuthenticationScheme;
    }
}
