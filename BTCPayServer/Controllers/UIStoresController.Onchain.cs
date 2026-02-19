using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Models.StoreViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    // Source-compat shims for code that still references UIStoresController wallet setup actions.
    [HttpGet("{storeId}/onchain/{cryptoCode}", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.SetupWallet instead.")]
    public Task<ActionResult> SetupWallet(WalletSetupViewModel vm)
    {
        return Task.FromResult<ActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.SetupWallet),
            "UIStoreOnChainWallets",
            new { vm.StoreId, vm.CryptoCode })!);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/import/{method?}", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.ImportWallet instead.")]
    public Task<IActionResult> ImportWallet(WalletSetupViewModel vm)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.ImportWallet),
            "UIStoreOnChainWallets",
            new { vm.StoreId, vm.CryptoCode, method = vm.Method })!);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/modify", Order = 1000)]
    [HttpPost("{storeId}/onchain/{cryptoCode}/import/{method}", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.UpdateWallet instead.")]
    public Task<IActionResult> UpdateWallet(WalletSetupViewModel vm)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.ImportWallet),
            "UIStoreOnChainWallets",
            new { vm.StoreId, vm.CryptoCode, method = vm.Method })!);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.GenerateWallet instead.")]
    public Task<IActionResult> GenerateWallet(WalletSetupViewModel vm)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.GenerateWallet),
            "UIStoreOnChainWallets",
            new { vm.StoreId, vm.CryptoCode, method = vm.Method })!);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/generate/{method}", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.GenerateWallet instead.")]
    public Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, WalletSetupMethod method, WalletSetupRequest request)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.GenerateWallet),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode, method })!);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/generate/confirm", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.GenerateWalletConfirm instead.")]
    public ActionResult GenerateWalletConfirm(string storeId, string cryptoCode)
    {
        return RedirectToAction(
            nameof(UIStoreOnChainWalletsController.GenerateWalletConfirm),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!;
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/settings", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.WalletSettings instead.")]
    public Task<IActionResult> WalletSettings(string storeId, string cryptoCode)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.WalletSettings),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/settings/wallet", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.UpdateWalletSettings instead.")]
    public Task<IActionResult> UpdateWalletSettings(WalletSettingsViewModel vm)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.WalletSettings),
            "UIStoreOnChainWallets",
            new { vm.StoreId, vm.CryptoCode })!);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/seed", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.WalletSeed instead.")]
    public Task<IActionResult> WalletSeed(string storeId, string cryptoCode, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.WalletSeed),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/replace", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.ReplaceWallet instead.")]
    public ActionResult ReplaceWallet(string storeId, string cryptoCode)
    {
        return RedirectToAction(
            nameof(UIStoreOnChainWalletsController.ReplaceWallet),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!;
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/replace", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.ConfirmReplaceWallet instead.")]
    public IActionResult ConfirmReplaceWallet(string storeId, string cryptoCode)
    {
        return RedirectToAction(
            nameof(UIStoreOnChainWalletsController.ConfirmReplaceWallet),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!;
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/delete", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.DeleteWallet instead.")]
    public ActionResult DeleteWallet(string storeId, string cryptoCode)
    {
        return RedirectToAction(
            nameof(UIStoreOnChainWalletsController.DeleteWallet),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!;
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/delete", Order = 1000)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Obsolete("Use UIStoreOnChainWalletsController.ConfirmDeleteWallet instead.")]
    public Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
    {
        return Task.FromResult<IActionResult>(RedirectToAction(
            nameof(UIStoreOnChainWalletsController.ConfirmDeleteWallet),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode })!);
    }
}
