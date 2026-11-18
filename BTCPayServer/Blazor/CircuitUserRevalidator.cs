#nullable enable
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Blazor;

/// <summary>
/// A Blazor circuit keeps the principal it connected with, so a component acting later must
/// confirm against the database that the account behind it is still allowed to act.
/// </summary>
public class CircuitUserRevalidator(IServiceScopeFactory serviceScopeFactory)
{
    public async Task<ApplicationUser?> Revalidate(ClaimsPrincipal? principal, string? requiredRole = null)
    {
        if (principal?.Identity?.IsAuthenticated is not true)
            return null;

        // A fresh scope: the circuit's own services cache the user loaded when it connected.
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await signInManager.ValidateSecurityStampAsync(principal);
        if (user is null)
            return null;

        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        if (!await userService.CanLogin(new UserService.CanLoginContext(user)))
            return null;

        if (requiredRole is not null && !await signInManager.UserManager.IsInRoleAsync(user, requiredRole))
            return null;

        if (requiredRole != Roles.ServerAdmin && principal.IsInRole(Roles.ServerAdmin) &&
            !await signInManager.UserManager.IsInRoleAsync(user, Roles.ServerAdmin))
            return null;

        return user;
    }
}
