#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorDashboardKit.Models;
using BTCPayServer.Plugins.Dashboard;
using BTCPayServer.Tests.Mocks;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class BlazorWidgetAccessControlTests
{
    private sealed class AllowAll : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    [Fact]
    public async Task NoRequiredPermissions_IsAllowed()
    {
        var sut = new BtcpayWidgetAccessControl(new AllowAll(), new NullScopeProvider());
        var ok = await sut.IsAllowedAsync(new WidgetDescriptor { Type = "Notes" }, new ClaimsPrincipal());
        Assert.True(ok);
    }

    [Fact]
    public async Task RequiredPermissions_NullUser_IsDenied()
    {
        var sut = new BtcpayWidgetAccessControl(new AllowAll(), new NullScopeProvider());
        var ok = await sut.IsAllowedAsync(
            new WidgetDescriptor { Type = "X", RequiredPermissions = new[] { "btcpay.store.canviewstoresettings" } },
            user: null);
        Assert.False(ok);
    }
}
