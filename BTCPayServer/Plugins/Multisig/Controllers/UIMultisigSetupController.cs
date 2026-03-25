#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
[Area(MultisigPlugin.Area)]
public class UIMultisigSetupController(
    StoreRepository storeRepository,
    ExplorerClientProvider explorerProvider,
    OnChainWalletSettingsAuthorization walletSettingsAuthorization,
    OnChainWalletSetupService onChainWalletSetupService,
    MultisigService multisigService,
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
        if (!await walletSettingsAuthorization.AuthorizeOnChainWalletSettings(HttpContext, User, vm.StoreId, vm.CryptoCode))
            return Forbid();

        var checkResult = GetContext(vm.CryptoCode, out _, out _);
        if (checkResult is not null)
            return checkResult;

        await multisigService.PopulateSetupViewModel(vm, HttpContext);
        return View("Multisig", vm);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/import/multisig")]
    public async Task<IActionResult> SetupMultisig(string storeId, string cryptoCode, MultisigSetupViewModel vm, string? command = null)
    {
        vm.StoreId = storeId;
        vm.CryptoCode = cryptoCode;

        if (!IsSupportedCryptoCode(vm.CryptoCode))
            return NotFound();
        if (!await walletSettingsAuthorization.AuthorizeOnChainWalletSettings(HttpContext, User, vm.StoreId, vm.CryptoCode))
            return Forbid();

        var checkResult = GetContext(vm.CryptoCode, out var store, out var network);
        if (checkResult is not null)
            return checkResult;

        if (vm.Confirmation)
            return await ConfirmMultisigSetup(vm, store!, network!);

        vm.MultisigStoreUsers = await multisigService.GetStoreUsers(vm.StoreId, vm.MultisigParticipantUserIds);

        return command?.ToLowerInvariant() switch
        {
            "create-request" => await CreateMultisigRequest(vm),
            "reset-request" => await ResetMultisigRequest(vm),
            "remove-signer" => await RemoveMultisigSigner(vm),
            "finalize-request" => await FinalizeMultisigRequest(vm, network!),
            _ => View("Multisig", vm)
        };
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
        if (!TryBuildPendingStrategy(vm.StoreId, vm.CryptoCode, pending, network, out var currentStrategy, out var validationError))
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
                await multisigNotificationService.SendWalletCreatedEmails(HttpContext, vm.StoreId, vm.CryptoCode, finalizedPendingSetting.Value.Pending);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send wallet-created multisig emails for store {StoreId}", vm.StoreId);
            }
        }

        TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;
        return RedirectToAction(nameof(BTCPayServer.Controllers.UIStoreOnChainWalletsController.WalletSettings), "UIStoreOnChainWallets", new { area = "", storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
    }

    private async Task<IActionResult> CreateMultisigRequest(MultisigSetupViewModel vm)
    {
        var selectedIds = (vm.MultisigParticipantUserIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

        var usersById = vm.MultisigStoreUsers.ToDictionary(u => u.UserId, u => u, StringComparer.Ordinal);
        if (!selectedIds.All(usersById.ContainsKey))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["One or more selected users are invalid."].Value);
            return View("Multisig", vm);
        }

        var requesterUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var requesterStoreUser = vm.MultisigStoreUsers.FirstOrDefault(u => string.Equals(u.UserId, requesterUserId, StringComparison.Ordinal));
        var requesterEmail = requesterStoreUser?.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var requesterName = requesterStoreUser?.Name;

        var settingName = MultisigService.GetPendingMultisigSettingName(vm.CryptoCode);
        var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(vm.StoreId, settingName);
        var pending = setting?.Value;
        var isNewRequest = pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow;
        if (isNewRequest)
        {
            if (!string.IsNullOrEmpty(vm.MultisigRequestId))
            {
                ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
                return View("Multisig", vm);
            }

            pending = new PendingMultisigSetupData
            {
                RequestId = Guid.NewGuid().ToString("N"),
                CryptoCode = vm.CryptoCode,
                RequestedByUserId = requesterUserId,
                RequestedByEmail = requesterEmail,
                RequestedByName = requesterName,
                ScriptType = scriptType,
                TotalSigners = totalSigners,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
            };
        }
        else
        {
            var activePending = pending!;
            if (!string.Equals(vm.MultisigRequestId, activePending.RequestId, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
                ApplyPendingContext(vm, activePending);
                return View("Multisig", vm);
            }

            var requestedScriptType = scriptType;
            var pendingScriptType = (activePending.ScriptType ?? "p2wsh").Trim();
            if (!string.Equals(requestedScriptType, pendingScriptType, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.MultisigScriptType), stringLocalizer["This request is configured for {0}. Reset request to change script type.", pendingScriptType.ToUpperInvariant()].Value);
                ApplyPendingContext(vm, activePending);
                return View("Multisig", vm);
            }

            if (totalSigners < activePending.TotalSigners)
            {
                ModelState.AddModelError(nameof(vm.MultisigTotalSigners), stringLocalizer["This request is configured for {0} signers. Reset request to reduce N.", activePending.TotalSigners].Value);
                ApplyPendingContext(vm, activePending);
                return View("Multisig", vm);
            }

            if (totalSigners > activePending.TotalSigners)
                activePending.TotalSigners = totalSigners;

            pending = activePending;
        }

        pending.Participants ??= new List<PendingMultisigSetupParticipantData>();

        var existingIds = pending.Participants.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        var newIds = selectedIds.Where(id => !existingIds.Contains(id)).ToArray();
        var resendIds = selectedIds
            .Where(existingIds.Contains)
            .Where(id => pending.Participants.Any(p =>
                string.Equals(p.UserId, id, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(p.AccountKey)))
            .ToArray();
        var availableSlots = pending.TotalSigners - pending.Participants.Count;
        if (availableSlots <= 0 && newIds.Length > 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["This request already has {0} signers. Replace a pending signer or reset request.", pending.TotalSigners].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }
        if (newIds.Length > availableSlots)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Only {0} signer slot(s) left for this request.", availableSlots].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        foreach (var selectedId in newIds)
        {
            var selectedUser = usersById[selectedId];
            pending.Participants.Add(new PendingMultisigSetupParticipantData
            {
                UserId = selectedUser.UserId,
                Email = selectedUser.Email,
                Name = selectedUser.Name
            });
        }

        if (pending.Participants.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select at least one signer."].Value);
            return View("Multisig", vm);
        }

        if (pending.Participants.Count != pending.TotalSigners)
        {
            var missing = pending.TotalSigners - pending.Participants.Count;
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select exactly {0} signers for this request. Missing {1} signer(s).", pending.TotalSigners, missing].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        var required = vm.MultisigRequiredSigners ?? pending.RequiredSigners;
        if (required <= 0 || required > pending.TotalSigners)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequiredSigners), stringLocalizer["Required signatures must be between 1 and total signers (N)."].Value);
            return View("Multisig", vm);
        }

        pending.RequiredSigners = required;
        pending.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        var updated = setting is null
            ? await storeRepository.TryCreateSettingAsync(vm.StoreId, settingName, pending)
            : await storeRepository.TryUpdateSettingAsync(vm.StoreId, settingName, setting.XMin, pending);
        if (!updated)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            return View("Multisig", vm);
        }

        await multisigNotificationService.SendSignerRequestEmails(HttpContext, vm.StoreId, vm.CryptoCode, pending, newIds.Concat(resendIds).Distinct(StringComparer.Ordinal).ToArray());

        TempData[WellKnownTempData.SuccessMessage] = isNewRequest
            ? stringLocalizer["Multisig signer requests were created."].Value
            : stringLocalizer["Multisig signer requests were updated."].Value;
        return RedirectToAction(nameof(SetupMultisig), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode, multisigRequestId = pending.RequestId });
    }

    private async Task<IActionResult> ResetMultisigRequest(MultisigSetupViewModel vm)
    {
        var settingName = MultisigService.GetPendingMultisigSettingName(vm.CryptoCode);
        var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(vm.StoreId, settingName);
        var pending = setting?.Value;
        if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow || !string.Equals(pending.RequestId, vm.MultisigRequestId, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            return View("Multisig", vm);
        }

        var xMin = setting!.XMin;
        if (!await storeRepository.TryDeleteSettingAsync(vm.StoreId, settingName, xMin))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Multisig request was reset."].Value;
        return RedirectToAction(nameof(SetupMultisig), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
    }

    private async Task<IActionResult> RemoveMultisigSigner(MultisigSetupViewModel vm)
    {
        var settingName = MultisigService.GetPendingMultisigSettingName(vm.CryptoCode);
        var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(vm.StoreId, settingName);
        var pending = setting?.Value;
        if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow || !string.Equals(pending.RequestId, vm.MultisigRequestId, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            return View("Multisig", vm);
        }

        if (string.IsNullOrWhiteSpace(vm.MultisigRemoveUserId))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select a signer to remove."].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        var removed = pending.Participants.RemoveAll(p => string.Equals(p.UserId, vm.MultisigRemoveUserId, StringComparison.Ordinal));
        if (removed == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Selected signer is not part of this request."].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        pending.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var xMin = setting!.XMin;
        if (!await storeRepository.TryUpdateSettingAsync(vm.StoreId, settingName, xMin, pending))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request changed. Reload the page and try again."].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Signer removed from request."].Value;
        return RedirectToAction(nameof(SetupMultisig), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode, multisigRequestId = pending.RequestId });
    }

    private async Task<IActionResult> FinalizeMultisigRequest(MultisigSetupViewModel vm, BTCPayNetwork network)
    {
        var pending = await multisigService.GetPendingMultisigSetup(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
        if (pending is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["The multisig request was not found or has expired."].Value);
            return View("Multisig", vm);
        }

        var requestedRequired = vm.MultisigRequiredSigners ?? pending.RequiredSigners;
        var requestedTotal = vm.MultisigTotalSigners ?? pending.TotalSigners;
        var requestedScriptType = (vm.MultisigScriptType ?? pending.ScriptType ?? "p2wsh").Trim();
        var pendingScriptType = (pending.ScriptType ?? "p2wsh").Trim();
        var requiredMismatch = requestedRequired != pending.RequiredSigners;
        var scriptMismatch = !string.Equals(requestedScriptType, pendingScriptType, StringComparison.OrdinalIgnoreCase);
        var totalReduced = requestedTotal < pending.TotalSigners;
        var totalExpanded = requestedTotal > pending.TotalSigners;
        if (requiredMismatch || scriptMismatch || totalReduced || totalExpanded)
        {
            var configLabel = $"{pending.RequiredSigners}-of-{pending.TotalSigners} ({pendingScriptType.ToUpperInvariant()})";
            var message = scriptMismatch
                ? stringLocalizer["This request is {0}. Reset request to change script type.", configLabel].Value
                : totalReduced
                    ? stringLocalizer["This request is {0}. Reset request to reduce total signers.", configLabel].Value
                    : totalExpanded
                        ? stringLocalizer["This request is {0}. To continue with {1}-of-{2} add {3} more {4}.", configLabel, requestedRequired, requestedTotal, requestedTotal - pending.TotalSigners, requestedTotal - pending.TotalSigners == 1 ? stringLocalizer["signer"].Value : stringLocalizer["signers"].Value].Value
                        : stringLocalizer["This request is {0}. To continue with {1}-of-{2}, click \"Send requests\".", configLabel, requestedRequired, requestedTotal].Value;
            ModelState.AddModelError(nameof(vm.MultisigRequestId), message);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        if (pending.Participants.Count != pending.TotalSigners || pending.Participants.Any(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), stringLocalizer["Complete signer collection before creating the multisig wallet."].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        var selectedIds = (vm.MultisigParticipantUserIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Count != pending.TotalSigners || pending.Participants.Any(p => !selectedIds.Contains(p.UserId)))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), stringLocalizer["Select exactly {0} signers from this request before creating the multisig wallet.", pending.TotalSigners].Value);
            ApplyPendingContext(vm, pending);
            return View("Multisig", vm);
        }

        vm.MultisigRequiredSigners = pending.RequiredSigners;
        vm.MultisigTotalSigners = pending.TotalSigners;
        vm.MultisigScriptType = pending.ScriptType;
        vm.MultisigSigners = pending.Participants.Select(p => p.AccountKey).ToArray();
        vm.MultisigSignerFingerprints = pending.Participants.Select(p => p.MasterFingerprint ?? string.Empty).ToArray();
        vm.MultisigSignerKeyPaths = pending.Participants.Select(p => p.AccountKeyPath ?? string.Empty).ToArray();
        ApplyPendingContext(vm, pending);

        if (!TryBuildPendingStrategy(vm.StoreId, vm.CryptoCode, pending, network, out var strategy, out var multisigValidationError))
        {
            ModelState.AddModelError(nameof(vm.DerivationScheme), multisigValidationError);
            return View("Multisig", vm);
        }

        vm.Config = onChainWalletSetupService.ProtectConfig(vm.CryptoCode, strategy);
        vm.Confirmation = true;
        vm.DerivationScheme = strategy.AccountDerivation.ToString();

        return ConfirmAddresses(vm, strategy, network);
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

    private bool TryBuildPendingStrategy(string storeId, string cryptoCode, PendingMultisigSetupData pending, BTCPayNetwork network, out DerivationSchemeSettings strategy, out string validationError)
    {
        strategy = default!;
        validationError = string.Empty;

        var validationVm = new MultisigSetupViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            MultisigRequestId = pending.RequestId,
            MultisigRequiredSigners = pending.RequiredSigners,
            MultisigTotalSigners = pending.TotalSigners,
            MultisigScriptType = pending.ScriptType,
            MultisigSigners = pending.Participants.Select(p => p.AccountKey).ToArray(),
            MultisigSignerFingerprints = pending.Participants.Select(p => p.MasterFingerprint ?? string.Empty).ToArray(),
            MultisigSignerKeyPaths = pending.Participants.Select(p => p.AccountKeyPath ?? string.Empty).ToArray()
        };

        if (!multisigService.TryBuildDerivationScheme(validationVm, network, out var multisigDerivation, out validationError))
            return false;

        strategy = BTCPayServer.Controllers.UIStoreOnChainWalletsController.ParseDerivationStrategy(multisigDerivation, network);
        strategy.Source = "ManualDerivationScheme";
        multisigService.ApplySignerOrigins(validationVm, strategy);
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

    private void ApplyPendingContext(MultisigSetupViewModel vm, PendingMultisigSetupData pending)
    {
        vm.MultisigPendingSetup = pending;
        vm.MultisigRequestId ??= pending.RequestId;
        vm.MultisigInviteLinks = multisigService.CreateInviteLinks(HttpContext, vm.StoreId, vm.CryptoCode, pending);
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

        return (pending, setting.XMin);
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

    private IActionResult? GetContext(string cryptoCode, out StoreData? store, out BTCPayNetwork? network)
    {
        store = HttpContext.GetStoreDataOrNull();
        network = explorerProvider.GetNetwork(cryptoCode);
        return store is null || network is null ? NotFound() : null;
    }
}
