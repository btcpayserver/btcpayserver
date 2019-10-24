using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security
{
    public static class Policies
    {
        public static AuthorizationOptions AddBTCPayPolicies(this AuthorizationOptions options)
        {
            options.AddPolicy(CanModifyStoreSettings.Key);
            options.AddPolicy(CanCreateInvoice.Key);
            options.AddPolicy(CanGetRates.Key);
            options.AddPolicy(CanModifyServerSettings.Key);
            return options;
        }

        public static void AddPolicy(this AuthorizationOptions options, string policy)
        {
            options.AddPolicy(policy, o => o.AddRequirements(new PolicyRequirement(policy)));
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

        public class CanGetRates
        {
            public const string Key = "btcpay.store.cangetrates";
        }
    }
}
