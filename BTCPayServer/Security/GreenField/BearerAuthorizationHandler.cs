#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;
using StoreData = BTCPayServer.Client.Models.StoreData;

namespace BTCPayServer.Security.GreenField;

public class BearerAuthorizationHandler(IOptionsMonitor<IdentityOptions> identityOptions)
    : AuthorizationHandler<PolicyRequirement>
{
    //TODO: In the future, we will add these store permissions to actual aspnet roles, and remove this class.
    private static readonly PermissionSet _serverAdminRolePermissions = new([Permission.Create(Policies.CanViewStoreSettings)]);

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
    {
        if (context.User.Identity?.AuthenticationType != AuthenticationSchemes.GreenfieldBearer)
            return;

        var userId = context.User.Claims.FirstOrDefault(c => c.Type == identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType)?.Value;
        if (string.IsNullOrEmpty(userId))
            return;

        var permissionSet = new PermissionSet();
        var success = false;
        var isAdmin = context.User.IsInRole(Roles.ServerAdmin);
        var storeId = context.Resource as string;
        var policy = requirement.Policy;
        var requiredUnscoped = false;
        if (policy.EndsWith(':'))
        {
            policy = policy[..^1];
            requiredUnscoped = true;
            storeId = null;
        }

        if (!string.IsNullOrEmpty(storeId))
        {
            var permissions = context.User.Claims.FirstOrDefault(c => c.Type == storeId)?.Value;
            if (!string.IsNullOrEmpty(permissions))
            {
                permissionSet = new PermissionSet(permissions.Split(',')
                    .Select(s => Permission.TryCreatePermission(s, storeId, out var permission) ? permission : null)
                    .Where(s => s != null).ToArray());
            }
        }

        if (Policies.IsServerPolicy(policy) && isAdmin)
        {
            success = true;
        }
        else if (Policies.IsUserPolicy(policy) && !string.IsNullOrEmpty(userId))
        {
            success = true;
        }
        else if (Policies.IsStorePolicy(policy) && !string.IsNullOrEmpty(storeId))
        {
            if (isAdmin && !string.IsNullOrEmpty(storeId))
            {
                success = _serverAdminRolePermissions.HasPermission(policy, storeId);
            }

            if (!success && permissionSet.HasPermission(policy, storeId))
            {
                success = true;
            }

            if (!success && requiredUnscoped && string.IsNullOrEmpty(storeId))
            {
                success = true;
            }
        }
        if (success)
        {
            context.Succeed(requirement);
        }
    }
}
