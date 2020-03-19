using System.Collections.Generic;
using BTCPayServer.Client;

namespace BTCPayServer.Security.APIKeys
{
    public static class APIKeyConstants
    {
        public const string AuthenticationType = "APIKey";

        public static class ClaimTypes
        {
            public const string Permission = "APIKey.Permission";
        }
    }
}
