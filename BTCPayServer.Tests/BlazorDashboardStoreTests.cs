#nullable enable
using BlazorDashboardKit.Models;
using BTCPayServer.Plugins.Dashboard;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class BlazorDashboardStoreTests
{
    [Fact]
    public void ParsesOwnerKey_StorePrefix()
    {
        Assert.True(BtcpayDashboardStore.TryParseOwner("store:abc", out var scope, out var id));
        Assert.Equal(BtcpayDashboardStore.OwnerScope.Store, scope);
        Assert.Equal("abc", id);
    }

    [Fact]
    public void ParsesOwnerKey_Server()
    {
        Assert.True(BtcpayDashboardStore.TryParseOwner("server", out var scope, out var id));
        Assert.Equal(BtcpayDashboardStore.OwnerScope.Server, scope);
        Assert.Null(id);
    }

    [Fact]
    public void RejectsUnknownOwnerKey()
    {
        Assert.False(BtcpayDashboardStore.TryParseOwner("", out _, out _));
        Assert.False(BtcpayDashboardStore.TryParseOwner("user:x", out _, out _));
        Assert.False(BtcpayDashboardStore.TryParseOwner("store:", out _, out _));
    }
}
