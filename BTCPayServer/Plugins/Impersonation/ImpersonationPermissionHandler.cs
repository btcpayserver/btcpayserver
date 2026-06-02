using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Impersonation;

public class ImpersonationPermissionHandler(IServiceScopeFactory services) :  IPermissionHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext authContext, PermissionAuthorizationContext permContext)
    {
        if (permContext.Permission.Policy == ImpersonationPlugin.CanImpersonateUser)
        {
            var isAdmin = authContext.User.IsInRole(Roles.ServerAdmin);
            if (await CanImpersonateUser(permContext, isAdmin))
                authContext.Succeed(permContext.Requirement);
        }
    }
    /// <summary>
    /// Determines if the current user can impersonate the target user.
    /// Rules:
    /// - User can always impersonate themselves
    /// - Non-admin users cannot impersonate others
    /// - ServerAdmins users can impersonate any user except other ServerAdmins
    /// - Returns false if the target user does not exist
    /// </summary>
    /// <param name="permContext"></param>
    /// <param name="isAdmin"></param>
    /// <returns></returns>
    private async Task<bool> CanImpersonateUser(PermissionAuthorizationContext permContext, bool isAdmin)
    {
        if (permContext.Permission.Scope is not string impersonatedUserId)
            return false;
        if (permContext.UserId == impersonatedUserId)
            return true;
        if (!isAdmin)
            return false;

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var impersonatedUser = await userManager.FindByIdAsync(impersonatedUserId);
        if (impersonatedUser is null)
            return false;
        return !await userManager.IsInRoleAsync(impersonatedUser, Roles.ServerAdmin);
    }
}
