using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security
{
    public static class Policies
    {
        public const string BitpayAuthentication = "Bitpay.Auth";
        public const string CookieAuthentication = "Identity.Application";
        public static AuthorizationOptions AddBTCPayPolicies(this AuthorizationOptions options)
        {
            AddClaim(options, CanModifyStoreSettings.Key);
            AddClaim(options, CanModifyServerSettings.Key);
            AddClaim(options, CanCreateInvoice.Key);
            return options;
        }

        private static void AddClaim(AuthorizationOptions options, string key)
        {
            options.AddPolicy(key, o => o.RequireClaim(key));
        }

        public class CanModifyServerSettings
        {
            public const string Key = "btcpay.store.canmodifyserversettings";
        }
        public class CanModifyStoreSettings
        {
            public const string Key = "btcpay.store.canmodifystoresettings";
        }

        public class CanCreateInvoice
        {
            public const string Key = "btcpay.store.cancreateinvoice";
        }
    }
}
