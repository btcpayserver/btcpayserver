using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security
{
    public static class ServerPolicies
    {
        public static AuthorizationOptions AddBTCPayPolicies(this AuthorizationOptions options)
        {
            foreach (var p in Policies.AllPolicies)
            {
                options.AddPolicy(p);
            }
            options.AddPolicy(Policies.CanModifyStoreSettingsUnscoped);
            options.AddPolicy(CanGetRates.Key);
            return options;
        }

        public static void AddPolicy(this AuthorizationOptions options, string policy)
        {
            options.AddPolicy(policy, o => o.AddRequirements(new PolicyRequirement(policy)));
        }
        public class CanGetRates
        {
            public const string Key = "btcpay.store.cangetrates";
        }
    }
}
