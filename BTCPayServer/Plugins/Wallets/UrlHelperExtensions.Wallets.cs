#nullable enable
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Wallets;

public static class UrlHelperExtensions
{
    public static string? WalletSend(this IUrlHelper helper, WalletId walletId) => helper.Action(nameof(UIWalletsController.WalletSend),  "UIWallets", new { area = WalletsPlugin.Area, walletId });
    public static string? WalletTransactions(this IUrlHelper helper, string walletId) => WalletTransactions(helper, WalletId.Parse(walletId));
    public static string? WalletTransactions(this IUrlHelper helper, WalletId walletId)
        => helper.Action(nameof(UIWalletsController.WalletTransactions), "UIWallets", new { area = WalletsPlugin.Area, walletId });
}
