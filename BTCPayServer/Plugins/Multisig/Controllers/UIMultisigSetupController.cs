#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Plugins.Wallets.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("stores")]
[Authorize(Policy = WalletPolicies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigSetupController(
    StoreRepository storeRepository,
    ExplorerClientProvider explorerProvider,
    OnChainWalletSetupService onChainWalletSetupService,
    MultisigService multisigService,
    IAuthorizationService authorizationService,
    MultisigNotificationService multisigNotificationService,
    IStringLocalizer stringLocalizer,
    ILogger<UIMultisigSetupController> logger) : Controller
{
    private static bool IsSupportedCryptoCode(string? cryptoCode) =>
        string.Equals(cryptoCode, "BTC", StringComparison.OrdinalIgnoreCase);

    [HttpGet("{storeId}/onchain/{cryptoCode}/import/multisig")]
    public async Task<IActionResult> SetupMultisig(string storeId, string cryptoCode, MultisigSetupViewModel vm)
    {
        vm.StoreId = storeId;
        vm.CryptoCode = cryptoCode;

        if (!IsSupportedCryptoCode(vm.CryptoCode))
            return NotFound();

        await multisigService.PopulateSetupViewModel(vm);
        return View("Multisig", vm);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/import/multisig")]
    public async Task<IActionResult> CreateMultisigRequest(string storeId, string cryptoCode, MultisigSetupViewModel vm)
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
            ModelState.AddModelError(nameof(vm.MultisigTotalSigners), stringLocalizer["Total signers must be between 1 and 15."].Value);
            return View("Multisig", vm);
        }

        var scriptType = NormalizeMultisigScriptType(vm.MultisigScriptType);
        if (scriptType is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigScriptType), stringLocalizer["Invalid multisig script type."].Value);
            return View("Multisig", vm);
        }
        vm.MultisigScriptType = scriptType;
        vm.MultisigStoreUsers = await multisigService.GetStoreUsers(vm.StoreId, selectedIds);
        var usersById = vm.MultisigStoreUsers.ToDictionary(u => u.UserId, u => u, StringComparer.Ordinal);
        if (!selectedIds.All(usersById.ContainsKey))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["One or more selected users are invalid."].Value);
            return View("Multisig", vm);
        }

        var requesterUserId = User.GetId();
        var requesterStoreUser = vm.MultisigStoreUsers.FirstOrDefault(u => string.Equals(u.UserId, requesterUserId, StringComparison.Ordinal));
        var requesterEmail = requesterStoreUser?.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

        if (selectedIds.Length == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select at least one signer."].Value);
            return View("Multisig", vm);
        }

        if (selectedIds.Length != totalSigners)
        {
            var missing = totalSigners - selectedIds.Length;
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select exactly {0} signers for this request. Missing {1} signer(s).", totalSigners, Math.Max(0, missing)].Value);
            return View("Multisig", vm);
        }

        var required = vm.MultisigRequiredSigners ?? 0;
        if (required <= 0 || required > totalSigners)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequiredSigners), stringLocalizer["Required signatures must be between 1 and total signers (N)."].Value);
            return View("Multisig", vm);
        }

        var pending = new PendingMultisigSetupData
        {
            RequestId = Guid.NewGuid().ToString("N"),
            StoreId = vm.StoreId,
            CryptoCode = vm.CryptoCode.ToUpperInvariant(),
            RequestedByEmail = requesterEmail,
            ScriptType = scriptType,
            RequiredSigners = required,
            TotalSigners = totalSigners,
            ReplacesExistingWallet = multisigService.HasOnChainWallet(store, vm.CryptoCode),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Participants = selectedIds
                .Select(selectedId =>
                {
                    var selectedUser = usersById[selectedId];
                    return new PendingMultisigSetupParticipantData
                    {
                        UserId = selectedUser.UserId,
                        Email = selectedUser.Email,
                        Name = selectedUser.Name
                    };
                })
                .ToList(),
            RequestBaseUrl = HttpContext.Request.GetRequestBaseUrl()
        };

        var settingName = MultisigService.GetPendingMultisigSettingName(vm.CryptoCode);
        var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(vm.StoreId, settingName);
        var updated = setting is null
            ? await storeRepository.TryCreateSettingAsync(vm.StoreId, settingName, pending)
            : await storeRepository.TryUpdateSettingAsync(vm.StoreId, settingName, setting.XMin, pending);
        if (!updated)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            return View("Multisig", vm);
        }

        await multisigNotificationService.EnsureDefaultEmailRules(vm.StoreId);

        await multisigNotificationService.PublishSignerKeyRequestedEvents(vm.StoreId, vm.CryptoCode, pending);

        TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Multisig signer requests were created."].Value;
        return RedirectToAction(nameof(UIMultisigStatusController.Status), "UIMultisigStatus", new { area = MultisigPlugin.Area, multisigSetupId = pending.RequestId });
    }

    [HttpPost("/multisig-setups/{multisigSetupId}/finalize")]
    public async Task<IActionResult> FinalizeMultisigSetup(string multisigSetupId, MultisigSetupViewModel vm)
    {
        var store = HttpContext.GetStoreData();
        var setupContext = await multisigService.GetPendingMultisigSetupContext(store.Id, multisigSetupId);
        if (setupContext is null)
            return NotFound();

        var pending = setupContext.Pending;
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
        if (!onChainWalletSetupService.TryParseProtectedConfig(vm.CryptoCode, vm.Config, out var strategy) || strategy is null)
        {
            ModelState.AddModelError(nameof(vm.Config), stringLocalizer["Config file was not in the correct format"].Value);
            return View("MultisigConfirm", vm);
        }

        if (string.IsNullOrEmpty(vm.MultisigRequestId))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request was not found or has expired."].Value);
            return View("MultisigConfirm", vm);
        }

        var pendingSetting = await GetPendingRequestWithVersion(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
        if (pendingSetting is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request was not found or has expired."].Value);
            return View("MultisigConfirm", vm);
        }

        var pending = pendingSetting.Value.Pending;
        if (!TryBuildPendingStrategy(pending, network, out var currentStrategy, out var validationError))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), validationError);
            return View("MultisigConfirm", vm);
        }

        if (!HasSameMultisigStrategy(strategy, currentStrategy))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["This multisig request changed. Please review it again before creating the wallet."].Value);
            vm.Config = onChainWalletSetupService.ProtectConfig(vm.CryptoCode, currentStrategy);
            return ConfirmAddresses(vm, currentStrategy, network);
        }

        var saveResult = await onChainWalletSetupService.SaveWallet(store, network, strategy, setupRequest: null);
        if (!saveResult.Success)
        {
            ModelState.AddModelError(nameof(vm.DerivationScheme), stringLocalizer[saveResult.ErrorMessage ?? "Wallet setup failed."].Value);
            return ConfirmAddresses(vm, strategy, network);
        }

        var finalizedPendingSetting = await GetPendingRequestWithVersion(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
        if (finalizedPendingSetting is not null &&
            await storeRepository.TryDeleteSettingAsync(
                vm.StoreId,
                MultisigService.GetPendingMultisigSettingName(vm.CryptoCode),
                finalizedPendingSetting.Value.XMin))
        {
            try
            {
                await multisigNotificationService.PublishWalletCreatedEvent(vm.StoreId, vm.CryptoCode, finalizedPendingSetting.Value.Pending);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send wallet-created multisig emails for store {StoreId}", vm.StoreId);
            }
        }

        TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;
        return RedirectToAction(nameof(BTCPayServer.Controllers.UIStoreOnChainWalletsController.WalletSettings), "UIStoreOnChainWallets", new { area = WalletsPlugin.Area, storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
    }

    private async Task<IActionResult> FinalizeMultisigRequest(PendingMultisigSetupData pending, BTCPayNetwork network)
    {

        if (pending.Participants.Count != pending.TotalSigners || pending.Participants.Any(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            ModelState.AddModelError(string.Empty, stringLocalizer["Complete signer collection before creating the multisig wallet."].Value);
            return await RenderSessionView(pending);
        }

        var eligibleParticipants = await multisigService.GetStoreUsers(pending.StoreId, pending.Participants.Select(p => p.UserId));
        var eligibleParticipantIds = eligibleParticipants.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        if (pending.Participants.Any(p => !eligibleParticipantIds.Contains(p.UserId)))
        {
            ModelState.AddModelError(string.Empty, stringLocalizer["One or more signers no longer have wallet signing permission."].Value);
            return await RenderSessionView(pending);
        }

        if (!TryBuildPendingStrategy(pending, network, out var strategy, out var multisigValidationError))
        {
            ModelState.AddModelError(string.Empty, multisigValidationError);
            return await RenderSessionView(pending);
        }

        var vm = new MultisigSetupViewModel
        {
            StoreId = pending.StoreId,
            CryptoCode = pending.CryptoCode,
            MultisigRequestId = pending.RequestId,
            MultisigRequiredSigners = pending.RequiredSigners,
            MultisigTotalSigners = pending.TotalSigners,
            MultisigScriptType = pending.ScriptType,
            Config = onChainWalletSetupService.ProtectConfig(pending.CryptoCode, strategy),
            Confirmation = true,
            DerivationScheme = strategy.AccountDerivation.ToString()
        };

        return ConfirmAddresses(vm, strategy, network);
    }

    private async Task<IActionResult> RenderSessionView(PendingMultisigSetupData pending)
    {
        var setupAccess = await authorizationService.GetSetupAccess(pending.StoreId, User, pending);
        if (!setupAccess.CanViewStatus)
            return Forbid();
        var model = multisigService.CreateInProgressViewModel(pending.StoreId, User.GetId(), pending, setupAccess.CanManageWalletSettings);
        return View("MultisigStatus", model);
    }

    private IActionResult ConfirmAddresses(MultisigSetupViewModel vm, DerivationSchemeSettings strategy, BTCPayNetwork network)
    {
        vm.DerivationScheme = strategy.AccountDerivation.ToString();
        vm.AddressSamples = new List<(string KeyPath, string Address)>();
        if (!string.IsNullOrEmpty(vm.DerivationScheme))
        {
            var result = BTCPayServer.Controllers.Greenfield.GreenfieldStoreOnChainPaymentMethodsController.GetPreviewResultData(0, 10, network, strategy.AccountDerivation);
            foreach (var sample in result.Addresses)
            {
                vm.AddressSamples.Add((sample.KeyPath, sample.Address));
            }
        }

        vm.Confirmation = true;
        return View("MultisigConfirm", vm);
    }

    private bool TryBuildPendingStrategy(PendingMultisigSetupData pending, BTCPayNetwork network, out DerivationSchemeSettings strategy, out string validationError)
    {
        strategy = default!;
        validationError = string.Empty;

        if (!multisigService.TryBuildDerivationScheme(
                pending.RequiredSigners,
                pending.TotalSigners,
                pending.ScriptType,
                pending.Participants,
                network,
                out var multisigDerivation,
                out validationError))
            return false;

        strategy = BTCPayServer.Controllers.UIStoreOnChainWalletsController.ParseDerivationStrategy(multisigDerivation, network);
        strategy.Source = "ManualDerivationScheme";
        multisigService.ApplySignerOrigins(pending.Participants, strategy);
        strategy.IsMultiSigOnServer = true;
        strategy.DefaultIncludeNonWitnessUtxo = true;
        multisigService.ApplySignerIdentities(pending, strategy, network);
        return true;
    }

    private static bool HasSameMultisigStrategy(DerivationSchemeSettings left, DerivationSchemeSettings right)
    {
        if (!string.Equals(left.AccountDerivation?.ToString(), right.AccountDerivation?.ToString(), StringComparison.Ordinal) ||
            left.IsMultiSigOnServer != right.IsMultiSigOnServer ||
            left.DefaultIncludeNonWitnessUtxo != right.DefaultIncludeNonWitnessUtxo)
            return false;

        var leftKeys = left.AccountKeySettings ?? Array.Empty<AccountKeySettings>();
        var rightKeys = right.AccountKeySettings ?? Array.Empty<AccountKeySettings>();
        if (leftKeys.Length != rightKeys.Length)
            return false;

        for (var i = 0; i < leftKeys.Length; i++)
        {
            if (!string.Equals(leftKeys[i].AccountKey?.ToString(), rightKeys[i].AccountKey?.ToString(), StringComparison.Ordinal) ||
                !string.Equals(leftKeys[i].AccountKeyPath?.ToString(), rightKeys[i].AccountKeyPath?.ToString(), StringComparison.Ordinal) ||
                !string.Equals(leftKeys[i].RootFingerprint?.ToString(), rightKeys[i].RootFingerprint?.ToString(), StringComparison.Ordinal) ||
                !string.Equals(leftKeys[i].SignerEmail, rightKeys[i].SignerEmail, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private async Task<(PendingMultisigSetupData Pending, uint XMin)?> GetPendingRequestWithVersion(string storeId, string cryptoCode, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(
            storeId,
            MultisigService.GetPendingMultisigSettingName(cryptoCode));
        var pending = setting?.Value;
        if (pending is null ||
            pending.ExpiresAt < DateTimeOffset.UtcNow ||
            !string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            return null;

        return (pending, setting!.XMin);
    }

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
