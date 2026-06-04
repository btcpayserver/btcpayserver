#nullable enable

using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("multisig-setups")]
[Authorize(Policy = WalletPolicies.CanViewWallet, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigSignerKeyController(
    StoreRepository storeRepository,
    ExplorerClientProvider explorerProvider,
    MultisigService multisigService,
    IAuthorizationService authorizationService,
    MultisigNotificationService multisigNotificationService,
    IStringLocalizer stringLocalizer) : Controller
{
    private const string HardwareInputMethod = "hardware";
    private const string ManualInputMethod = "manual";
    private static bool IsSupportedCryptoCode(string? cryptoCode) =>
        string.Equals(cryptoCode, "BTC", StringComparison.OrdinalIgnoreCase);
    private static string? NormalizeInputMethod(string? method) =>
        string.Equals(method, HardwareInputMethod, StringComparison.OrdinalIgnoreCase) ? HardwareInputMethod :
        string.Equals(method, ManualInputMethod, StringComparison.OrdinalIgnoreCase) ? ManualInputMethod :
        null;
    private const int UpdateRetries = 5;

    private enum SignerKeyLoadStatus
    {
        Ok,
        Invalid,
        Forbidden
    }

    private sealed class SignerKeyLoadResult
    {
        public SignerKeyLoadStatus Status { get; init; }
        public string? UserId { get; init; }
        public MultisigSignerKeyViewModel? ViewModel { get; init; }
    }

    [HttpGet("{multisigSetupId}/signer-key")]
    public async Task<IActionResult> SubmitMultisigSigner(string multisigSetupId, string? method = null)
    {
        var result = await LoadSignerKeyViewModel(multisigSetupId);
        if (result.Status is SignerKeyLoadStatus.Forbidden)
            return Forbid();
        if (result.Status is not SignerKeyLoadStatus.Ok)
            return NotFound();
        var vm = result.ViewModel!;
        if (!string.IsNullOrWhiteSpace(vm.DisplayAccountKey))
        {
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Your signer key is submitted."].Value;
            return RedirectToAction(nameof(UIMultisigStatusController.Status), "UIMultisigStatus", new { area = MultisigPlugin.Area, multisigSetupId = vm.RequestId });
        }
        vm.InputMethod = NormalizeInputMethod(method);
        return View("MultisigSignerKey", vm);
    }

    [HttpPost("{multisigSetupId}/signer-key")]
    public async Task<IActionResult> SubmitMultisigSigner(string multisigSetupId, MultisigSignerKeyViewModel vm)
    {
        var result = await LoadSignerKeyViewModel(multisigSetupId);
        if (result.Status is SignerKeyLoadStatus.Forbidden)
            return Forbid();
        if (result.Status is not SignerKeyLoadStatus.Ok)
            return NotFound();
        var current = result.ViewModel!;
        var currentUserId = result.UserId!;

        var network = explorerProvider.GetNetwork(current.CryptoCode);
        if (network is null)
            return NotFound();

        current.DisplayAccountKey = vm.DisplayAccountKey?.Trim();
        current.MasterFingerprint = vm.MasterFingerprint?.Trim();
        current.AccountKeyPath = vm.AccountKeyPath?.Trim();
        current.InputMethod = NormalizeInputMethod(vm.InputMethod) ?? ManualInputMethod;

        BitcoinExtPubKey? accountKey = null;
        if (string.IsNullOrWhiteSpace(current.DisplayAccountKey))
            ModelState.AddModelError(nameof(vm.DisplayAccountKey), stringLocalizer["Please provide your account key."].Value);
        else
        {
            try
            {
                accountKey = new BitcoinExtPubKey(current.DisplayAccountKey, network.NBitcoinNetwork);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, stringLocalizer["Invalid account key format."].Value);
            }
        }
        if (!string.IsNullOrWhiteSpace(current.MasterFingerprint) && !Regex.IsMatch(current.MasterFingerprint, "^[0-9a-fA-F]{8}$"))
            ModelState.AddModelError(nameof(vm.MasterFingerprint), stringLocalizer["Invalid fingerprint format."].Value);
        if (!string.IsNullOrWhiteSpace(current.AccountKeyPath))
        {
            if (!MultisigService.TryParseKeyPath(current.AccountKeyPath, out var accountKeyPath))
                ModelState.AddModelError(nameof(vm.AccountKeyPath), stringLocalizer["Invalid account key path."].Value);
            else
                current.AccountKeyPath = $"m/{accountKeyPath}";
        }

        var hasFingerprint = !string.IsNullOrWhiteSpace(current.MasterFingerprint);
        var hasAccountKeyPath = !string.IsNullOrWhiteSpace(current.AccountKeyPath);
        if (hasFingerprint && !hasAccountKeyPath)
            ModelState.AddModelError(nameof(vm.AccountKeyPath), stringLocalizer["Provide account key path when fingerprint is set."].Value);
        if (hasAccountKeyPath && !hasFingerprint)
            ModelState.AddModelError(nameof(vm.MasterFingerprint), stringLocalizer["Provide fingerprint when account key path is set."].Value);

        if (!ModelState.IsValid)
            return View("MultisigSignerKey", current);

        var normalizedAccountKey = accountKey!.ToString();
        for (var attempt = 0; attempt < UpdateRetries; attempt++)
        {
            var setupContext = await multisigService.GetPendingMultisigSetupContext(current.RequestId);
            if (setupContext is null)
                return NotFound();
            var pending = setupContext.Pending;

            var participant = pending.Participants.Find(p => string.Equals(p.UserId, currentUserId, StringComparison.Ordinal));
            if (participant is null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(participant.AccountKey))
            {
                TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Your signer key is submitted."].Value;
                return RedirectToAction(nameof(UIMultisigStatusController.Status), "UIMultisigStatus", new { area = MultisigPlugin.Area, multisigSetupId = current.RequestId });
            }

            var duplicateKeyFound = pending.Participants
                .Where(p => !string.Equals(p.UserId, currentUserId, StringComparison.Ordinal))
                .Any(p =>
                    TryParseAccountKey(p.AccountKey, network, out var participantKey) &&
                    participantKey.ToString() == normalizedAccountKey);
            if (duplicateKeyFound)
            {
                ModelState.AddModelError(string.Empty, stringLocalizer["This signer key is already used in this multisig request."].Value);
                return View("MultisigSignerKey", current);
            }

            participant.AccountKey = current.DisplayAccountKey;
            participant.MasterFingerprint = current.MasterFingerprint;
            participant.AccountKeyPath = current.AccountKeyPath;
            if (!await storeRepository.TryUpdateSettingAsync(setupContext.StoreId, setupContext.SettingName, setupContext.XMin, pending))
                continue;

            await multisigNotificationService.PublishSignerKeySubmittedEvent(setupContext.StoreId, pending, participant);
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Signer key submitted successfully."].Value;
            return RedirectToAction(nameof(UIMultisigStatusController.Status), "UIMultisigStatus", new { area = MultisigPlugin.Area, multisigSetupId = current.RequestId });
        }

        ModelState.AddModelError(string.Empty, stringLocalizer["This multisig request changed while you were submitting your signer key. Please try again."].Value);
        return View("MultisigSignerKey", current);
    }

    private async Task<SignerKeyLoadResult> LoadSignerKeyViewModel(string multisigSetupId)
    {
        var currentUserId = User.GetId();
        var setupContext = await multisigService.GetPendingMultisigSetupContext(multisigSetupId);
        if (setupContext is null || !IsSupportedCryptoCode(setupContext.Pending.CryptoCode))
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Invalid };
        var pending = setupContext.Pending;

        var access = await authorizationService.GetSetupAccess(setupContext.StoreId, User, pending);
        if (!access.CanSignWalletTransactions || !access.IsParticipant)
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Forbidden };

        var participant = pending.Participants.Find(p => string.Equals(p.UserId, currentUserId, StringComparison.Ordinal));
        if (participant is null)
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Invalid };

        return new SignerKeyLoadResult
        {
            Status = SignerKeyLoadStatus.Ok,
            UserId = participant.UserId,
            ViewModel = new MultisigSignerKeyViewModel
            {
                CryptoCode = pending.CryptoCode,
                RequestId = pending.RequestId,
                RequiredSigners = pending.RequiredSigners,
                TotalSigners = pending.TotalSigners,
                ScriptType = pending.ScriptType,
                DisplayAccountKey = participant.AccountKey,
                MasterFingerprint = participant.MasterFingerprint,
                AccountKeyPath = participant.AccountKeyPath
            }
        };
    }

    private static bool TryParseAccountKey(string? accountKey, BTCPayNetwork network, out BitcoinExtPubKey parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(accountKey) || network.NBitcoinNetwork is null)
            return false;
        try
        {
            parsed = new BitcoinExtPubKey(accountKey.Trim(), network.NBitcoinNetwork);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
