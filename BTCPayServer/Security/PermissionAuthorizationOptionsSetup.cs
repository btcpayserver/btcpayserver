using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security
{
    public class PermissionAuthorizationOptionsSetup(PermissionService permissionService) : IConfigureOptions<AuthorizationOptions>
    {
        public void Configure(AuthorizationOptions options)
        {
            foreach (var def in permissionService.Definitions.Values)
            {
                options.AddPolicy(def.Policy, o => o.AddRequirements(new PolicyRequirement(def.Policy)));
                options.AddPolicy(def.Policy + ":", o => o.AddRequirements(new PolicyRequirement(def.Policy, true)));
            }
            options.AddPolicy(ServerPolicies.CanGetRates.Key, o => o.AddRequirements(new PolicyRequirement(ServerPolicies.CanGetRates.Key)));
        }
    }
}
