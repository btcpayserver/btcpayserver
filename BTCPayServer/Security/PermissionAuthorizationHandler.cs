#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security;

public class PermissionAuthorizationHandler(
    PermissionService permissionService,
    IHttpContextAccessor httpContext,
    IEnumerable<IPermissionHandler> permissionHandlers,
    IEnumerable<IPermissionScopeProvider> implicitScopeProviders,
    UserManager<ApplicationUser> userManager)
    : AuthorizationHandler<PolicyRequirement>
{
    public const string PolicyRequirementKey = nameof(PolicyRequirementKey);
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
    {
        if (context.User.Identity is not ({ AuthenticationType: AuthenticationSchemes.Cookie } or { AuthenticationType: GreenfieldConstants.AuthenticationType }))
            return;
        var userId = userManager.GetUserId(context.User);
        if (userId is null || httpContext.HttpContext is null)
            return;
        httpContext.HttpContext.Items[PolicyRequirementKey] = requirement;

        string? scope = null;
        var explicitScope = false;
        if (!requirement.RequireUnscoped)
        {
            scope = context.Resource as string;
            explicitScope = scope is not null;
            if (!explicitScope)
                scope = await GetImplicitScope(userId, context, requirement, httpContext.HttpContext);
        }
        await Handle(context, requirement, scope, userId, explicitScope, httpContext.HttpContext);
    }

    private async Task Handle(AuthorizationHandlerContext context, PolicyRequirement requirement, string? scope,
        string userId, bool explicitScope, HttpContext httpContext2)
    {
        var ctx = new PermissionAuthorizationContext(requirement, scope, userId, httpContext2)
        {
            ExplicitScope = explicitScope
        };

        if (scope is not null || ctx.Requirement.RequireUnscoped)
        {
            if (!context.HasPermission(ctx.Permission, permissionService))
                return;
        }
        // Imagine `ListStores` with `btcpay.store.canviewstoresettings`.
        // Because it lists all the stores, the permission doesn't match any scope.
        else
        {
            // Now imagine that the api key has "btcpay.store.canviewstoresettings:StoreA"
            // The route should still be accessible by the API key.
            // However, the action is responsible for only allowing `StoreA` to be shown in the list.
            if (!context.HasPermission(ctx.Permission, permissionService, anyScope: true))
                return;
        }

        foreach (var handler in permissionHandlers)
        {
            await handler.HandleAsync(context, ctx);
        }
    }

    protected async Task<string?> GetImplicitScope(string userId, AuthorizationHandlerContext context, PolicyRequirement requirement, HttpContext httpContext2)
    {
        var ctx = new ScopeProviderAuthorizationContext(userId, requirement, httpContext2);
        foreach (var implicitScopeProvider in implicitScopeProviders)
        {
            var scope = await implicitScopeProvider.GetScope(context, ctx);
            if (scope is not null)
                return scope;
        }
        return null;
    }
}
