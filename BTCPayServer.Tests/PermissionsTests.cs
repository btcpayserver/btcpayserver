using BTCPayServer.Client;
using BTCPayServer.Services;
using Xunit;

namespace BTCPayServer.Tests;

public class PermissionsTests
{
    [Fact]
    public void CanParseWalletStorePermissionWithScope()
    {
        Assert.True(Permission.TryParse("btcpay.store.canviewwallet:store1", out var walletPermission));
        Assert.NotNull(walletPermission);
        Assert.Equal(PolicyType.Store, walletPermission.Type);
        Assert.Equal("store1", walletPermission.Scope);
    }

    [Fact]
    public void RejectsEmptyScope()
    {
        Assert.False(Permission.TryParse("btcpay.store.canmodifystoresettings:", out _));
    }

    [Fact]
    public void PolicyDefinitionAcceptsPluginStorePolicies()
    {
        var definition = new PolicyDefinition(
            "btcpay.plugin.store.example",
            new PermissionDisplay("Example", "Example permission"));

        Assert.Equal("btcpay.plugin.store.example", definition.Policy);
        Assert.Equal(PolicyType.Store, definition.Type);
    }
}
