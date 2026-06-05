#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("stores")]
[Authorize(Policy = WalletPolicies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigWalletsController(
    StoreRepository storeRepository,
    ExplorerClientProvider explorerProvider,
    BTCPayWalletProvider walletProvider,
    MultisigService multisigService,
    MultisigNotificationService multisigNotificationService,
    PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
    IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    private static bool IsSupportedCryptoCode(string? cryptoCode) =>
        string.Equals(cryptoCode, "BTC", StringComparison.OrdinalIgnoreCase);

    [HttpGet("{storeId}/onchain/{cryptoCode}/import/multisig")]
    public async Task<IActionResult> SetupMultisig(string storeId, string cryptoCode)
    {
        var vm = new MultisigSetupViewModel();
        vm.StoreId = storeId;
        vm.CryptoCode = cryptoCode;

        if (!IsSupportedCryptoCode(vm.CryptoCode))
            return NotFound();

        await multisigService.PopulateSetupViewModel(vm);
        return View(vm);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/import/multisig")]
    public async Task<IActionResult> SetupMultisig(string storeId, string cryptoCode, MultisigSetupViewModel vm)
    {
        var store = HttpContext.GetStoreData();
        vm.StoreId = store.Id;
        vm.CryptoCode = cryptoCode;

        if (!IsSupportedCryptoCode(vm.CryptoCode))
            return NotFound();

        var selectedIds = (vm.MultisigParticipantUserIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        vm.MultisigStoreUsers = await multisigService.GetStoreUsers(vm.StoreId, selectedIds);
        var totalSigners = vm.MultisigTotalSigners ?? 0;
        if (totalSigners is <= 0 or > 15)
        {
            ModelState.AddModelError(nameof(vm.MultisigTotalSigners), StringLocalizer["Total signers must be between 1 and 15."].Value);
            return View(vm);
        }

        var scriptType = NormalizeMultisigScriptType(vm.MultisigScriptType);
        if (scriptType is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigScriptType), StringLocalizer["Invalid multisig script type."].Value);
            return View(vm);
        }
        vm.MultisigScriptType = scriptType;
        var usersById = vm.MultisigStoreUsers.ToDictionary(u => u.UserId, u => u, StringComparer.Ordinal);
        if (!selectedIds.All(usersById.ContainsKey))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), StringLocalizer["One or more selected users are invalid."].Value);
            return View(vm);
        }

        if (selectedIds.Length == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), StringLocalizer["Select at least one signer."].Value);
            return View(vm);
        }

        if (selectedIds.Length != totalSigners)
        {
            var missing = totalSigners - selectedIds.Length;
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), StringLocalizer["Select exactly {0} signers for this request. Missing {1} signer(s).", totalSigners, Math.Max(0, missing)].Value);
            return View(vm);
        }

        var required = vm.MultisigRequiredSigners ?? 0;
        if (required <= 0 || required > totalSigners)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequiredSigners), StringLocalizer["Required signatures must be between 1 and total signers (N)."].Value);
            return View(vm);
        }

        var pending = new MultisigSetupData
        {
            RequestId = Guid.NewGuid().ToString("N"),
            StoreId = vm.StoreId,
            CryptoCode = vm.CryptoCode.ToUpperInvariant(),
            RequestedByUserId = User.GetId(),
            ScriptType = scriptType,
            RequiredSigners = required,
            TotalSigners = totalSigners,
            ReplacesExistingWallet = multisigService.HasOnChainWallet(store, vm.CryptoCode),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Participants = selectedIds
                .Select(selectedId =>
                    new PendingMultisigSetupParticipantData
                    {
                        UserId = selectedId
                    })
                .ToList(),
            RequestBaseUrl = HttpContext.Request.GetRequestBaseUrl()
        };

        await multisigService.SavePendingMultisigSetup(pending);

        await multisigNotificationService.EnsureDefaultEmailRules(vm.StoreId);

        await multisigNotificationService.PublishSignerKeyRequestedEvents(pending);

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Multisig signer requests were created."].Value;
        return RedirectToMultisigSetup(pending.RequestId);
    }

    [HttpPost("/multisig-setups/{multisigSetupId}/finalize")]
    public async Task<IActionResult> FinalizeMultisigSetup(string multisigSetupId, MultisigSetupViewModel vm)
    {
        var store = HttpContext.GetStoreData();
        var pending = await multisigService.GetPendingMultisigSetupContext(store.Id, multisigSetupId);
        if (pending is null)
            return NotFound();

        vm.StoreId = pending.StoreId;
        vm.CryptoCode = pending.CryptoCode;
        vm.MultisigRequestId = pending.RequestId;

        var network = explorerProvider.GetNetwork(vm.CryptoCode);
        if (network is null)
            return NotFound();

        return vm.Confirmation
            ? await ConfirmMultisigSetup(vm, store, network)
            : await FinalizeMultisigRequest(pending, network);
    }

    private async Task<IActionResult> ConfirmMultisigSetup(MultisigSetupViewModel vm, StoreData store, BTCPayNetwork network)
    {
        if (string.IsNullOrEmpty(vm.MultisigRequestId))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), StringLocalizer["The multisig request was not found or has expired."].Value);
            return View("MultisigConfirm", vm);
        }

        var pending = await multisigService.GetPendingMultisigSetupContext(vm.StoreId, vm.MultisigRequestId);
        if (pending is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), StringLocalizer["The multisig request was not found or has expired."].Value);
            return View("MultisigConfirm", vm);
        }

        var strategy = pending.GetDiredivationSchemeSettings(network);
        var wallet = walletProvider.GetWallet(network);
        if (wallet is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), StringLocalizer["Wallet is not available."].Value);
            return View("MultisigConfirm", vm);
        }

        await wallet.TrackAsync(strategy.AccountDerivation);
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        store.SetPaymentMethodConfig(paymentMethodHandlerDictionary[paymentMethodId], strategy);
        var storeBlob = store.GetStoreBlob();
        storeBlob.SetExcluded(paymentMethodId, false);
        store.SetStoreBlob(storeBlob);
        await storeRepository.UpdateStore(store);

        var finalizedPendingSetting = await multisigService.GetPendingMultisigSetupContext(vm.StoreId, vm.MultisigRequestId);
        if (finalizedPendingSetting is not null)
        {
            await multisigService.DeletePendingMultisigSetup(vm.StoreId, vm.MultisigRequestId);
            multisigNotificationService.PublishWalletCreatedEvent(finalizedPendingSetting);
        }
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;
        return RedirectToAction(nameof(BTCPayServer.Controllers.UIStoreOnChainWalletsController.WalletSettings), "UIStoreOnChainWallets", new { area = WalletsPlugin.Area, storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
    }

    private async Task<IActionResult> FinalizeMultisigRequest(MultisigSetupData pending, BTCPayNetwork network)
    {
        if (pending.Participants.Count != pending.TotalSigners || pending.Participants.Any(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Complete signer collection before creating the multisig wallet."].Value;
            return RedirectToMultisigSetup(pending.RequestId);
        }

        var eligibleParticipants = await multisigService.GetStoreUsers(pending.StoreId, pending.Participants.Select(p => p.UserId));
        var eligibleParticipantIds = eligibleParticipants.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        if (pending.Participants.Any(p => !eligibleParticipantIds.Contains(p.UserId)))
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["One or more signers no longer have wallet signing permission."].Value;
            return RedirectToMultisigSetup(pending.RequestId);
        }

        var strategy = pending.GetDiredivationSchemeSettings(network);
        var vm = new MultisigSetupViewModel
        {
            MultisigRequestId = pending.RequestId,
            Confirmation = true
        };
        vm.AddressSamples = new List<(string KeyPath, string Address)>();
        var result = BTCPayServer.Controllers.Greenfield.GreenfieldStoreOnChainPaymentMethodsController.GetPreviewResultData(0, 10, network, strategy.AccountDerivation);
        foreach (var sample in result.Addresses)
        {
            vm.AddressSamples.Add((sample.KeyPath, sample.Address));
        }
        return View("MultisigConfirm", vm);
    }

    IActionResult RedirectToMultisigSetup(string multisigSetupId)
    => RedirectToAction(nameof(UIMultisigSetupController.SetupMultisigStatus), "UIMultisigSetup", new { multisigSetupId });

    private static string? NormalizeMultisigScriptType(string? scriptType)
    {
        return scriptType?.Trim().ToLowerInvariant() switch
        {
            null or "" => "p2wsh",
            "p2wsh" => "p2wsh",
            "p2sh-p2wsh" => "p2sh-p2wsh",
            "p2sh" => "p2sh",
            _ => null
        };
    }
}
