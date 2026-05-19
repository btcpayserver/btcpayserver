#nullable enable
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorDashboardKit.Abstractions;
using BlazorDashboardKit.Models;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Plugins.Dashboard;

/// <summary>
/// Maps a widget descriptor's opaque <c>RequiredPermissions</c> tokens to BTCPay
/// policies. Store-scoped policies authorize against the current store id (from
/// the request scope); server-scoped pass null — mirrors the permission logic the
/// inlined BaseWidgetComponent used before the kit extraction.
/// </summary>
public sealed class BtcpayWidgetAccessControl : IWidgetAccessControl
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IScopeProvider _scopeProvider;

    public BtcpayWidgetAccessControl(IAuthorizationService authorizationService, IScopeProvider scopeProvider)
    {
        _authorizationService = authorizationService;
        _scopeProvider = scopeProvider;
    }

    public async Task<bool> IsAllowedAsync(WidgetDescriptor descriptor, ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        if (descriptor.RequiredPermissions.Length == 0)
            return true;
        if (user is null)
            return false;

        var storeId = _scopeProvider.GetCurrentStoreId();
        foreach (var permission in descriptor.RequiredPermissions)
        {
            object? resource = Permission.TryGetPolicyType(permission) == PolicyType.Store
                               && !string.IsNullOrEmpty(storeId)
                ? storeId
                : null;
            var result = await _authorizationService.AuthorizeAsync(user, resource, new PolicyRequirement(permission));
            if (!result.Succeeded)
                return false;
        }
        return true;
    }
}
