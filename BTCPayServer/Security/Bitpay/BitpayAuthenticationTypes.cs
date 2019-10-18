using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Security.Bitpay
{
    public class BitpayAuthenticationTypes
    {
        public const string ApiKeyAuthentication = "Bitpay.APIKey";
        public const string SinAuthentication = "Bitpay.SIN";
        public const string Anonymous = "Bitpay.Anonymous";
    }
}
