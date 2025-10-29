#nullable enable
using NBitcoin;

namespace BTCPayServer.Abstractions.Models;

public record WellKnownCategories(string CategoryId)
{
    public static readonly WellKnownCategories Server = new("BTCPayServer.Views.Server.ServerNavPages");
    public static readonly WellKnownCategories Store = new("BTCPayServer.Views.Stores.StoreNavPages");
    public static readonly WellKnownCategories Wallet = new("BTCPayServer.Views.Wallets.WalletsNavPages");
    public static WellKnownCategories ForWallet(string cryptoCode) => new(
        $"BTCPayServer.Views.Wallets.WalletsNavPages.{cryptoCode.ToUpperInvariant()}");
    public static WellKnownCategories ForLightning(string cryptoCode) => new(
        $"LightningPages.{cryptoCode.ToUpperInvariant()}");
}

public class LayoutModel(string menuItemId, string? title = null)
{
    public static string Map(WellKnownCategories c) => c.CategoryId;

    public LayoutModel SetCategory(WellKnownCategories category)
    {
        ActiveCategory = Map(category);
        return this;
    }
    public LayoutModel SetCategory(string category)
    {
        ActiveCategory = category;
        return this;
    }

    public string? ActiveCategory { get; set; }
    public string MenuItemId { get; set; } = menuItemId;
    public string? Title { get; set; } = title;
    public string? SubMenuItemId { get; set; }
}
