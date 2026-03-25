#nullable enable

using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
[Area(MultisigPlugin.Area)]
public class UIMultisigInviteController(
    StoreRepository storeRepository,
    ExplorerClientProvider explorerProvider,
    MultisigService multisigService,
    MultisigNotificationService multisigNotificationService,
    IStringLocalizer stringLocalizer) : Controller
{
    private static bool IsSupportedCryptoCode(string? cryptoCode) =>
        string.Equals(cryptoCode, "BTC", StringComparison.OrdinalIgnoreCase);
    private const int UpdateRetries = 5;

    private enum InviteLoadStatus
    {
        Ok,
        Invalid,
        WrongUser
    }

    private sealed class InviteLoadResult
    {
        public InviteLoadStatus Status { get; init; }
        public MultisigInviteViewModel? ViewModel { get; init; }
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/multisig/invite/{**token}")]
    public async Task<IActionResult> SubmitMultisigSigner(string storeId, string cryptoCode, string token)
    {
        if (!IsSupportedCryptoCode(cryptoCode))
            return NotFound();
        var result = await LoadInviteViewModel(storeId, cryptoCode, token);
        if (result.Status is InviteLoadStatus.WrongUser)
            return RedirectToAction("Login", "UIAccount", new { area = "", returnUrl = Request.PathBase + Request.Path + Request.QueryString });
        if (result.Status is not InviteLoadStatus.Ok)
            return NotFound();
        return View("MultisigInvite", result.ViewModel!);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/multisig/invite/{**token}")]
    public async Task<IActionResult> SubmitMultisigSigner(string storeId, string cryptoCode, string token, MultisigInviteViewModel vm)
    {
        if (!IsSupportedCryptoCode(cryptoCode))
            return NotFound();
        var result = await LoadInviteViewModel(storeId, cryptoCode, token);
        if (result.Status is InviteLoadStatus.WrongUser)
            return RedirectToAction("Login", "UIAccount", new { area = "", returnUrl = Request.PathBase + Request.Path + Request.QueryString });
        if (result.Status is not InviteLoadStatus.Ok)
            return NotFound();
        var current = result.ViewModel!;

        var network = explorerProvider.GetNetwork(cryptoCode);
        if (network is null)
            return NotFound();

        current.AccountKey = vm.AccountKey?.Trim();
        current.MasterFingerprint = vm.MasterFingerprint?.Trim();
        current.AccountKeyPath = vm.AccountKeyPath?.Trim();

        if (string.IsNullOrWhiteSpace(current.AccountKey))
            ModelState.AddModelError(nameof(vm.AccountKey), stringLocalizer["Please provide your account key."].Value);
        else if (!multisigService.TryNormalizeAccountKeyForNetwork(current.AccountKey, network, out var normalizedAccountKey))
            ModelState.AddModelError(string.Empty, stringLocalizer["Invalid account key format."].Value);
        else
            current.AccountKey = normalizedAccountKey;
        if (!string.IsNullOrWhiteSpace(current.MasterFingerprint) && !Regex.IsMatch(current.MasterFingerprint, "^[0-9a-fA-F]{8}$"))
            ModelState.AddModelError(nameof(vm.MasterFingerprint), stringLocalizer["Invalid fingerprint format."].Value);
        if (!string.IsNullOrWhiteSpace(current.AccountKeyPath))
        {
            var normalizedPath = MultisigService.NormalizePath(current.AccountKeyPath);
            if (!KeyPath.TryParse(normalizedPath, out _))
                ModelState.AddModelError(nameof(vm.AccountKeyPath), stringLocalizer["Invalid account key path."].Value);
            else
                current.AccountKeyPath = $"m/{normalizedPath}";
        }

        var hasFingerprint = !string.IsNullOrWhiteSpace(current.MasterFingerprint);
        var hasAccountKeyPath = !string.IsNullOrWhiteSpace(current.AccountKeyPath);
        if (hasFingerprint && !hasAccountKeyPath)
            ModelState.AddModelError(nameof(vm.AccountKeyPath), stringLocalizer["Provide account key path when fingerprint is set."].Value);
        if (hasAccountKeyPath && !hasFingerprint)
            ModelState.AddModelError(nameof(vm.MasterFingerprint), stringLocalizer["Provide fingerprint when account key path is set."].Value);

        if (!ModelState.IsValid)
            return View("MultisigInvite", current);

        var settingName = MultisigService.GetPendingMultisigSettingName(cryptoCode);
        for (var attempt = 0; attempt < UpdateRetries; attempt++)
        {
            var setting = await storeRepository.GetSettingWithVersionAsync<PendingMultisigSetupData>(storeId, settingName);
            if (setting is null || !string.Equals(setting.Value.RequestId, current.RequestId, StringComparison.Ordinal))
                return NotFound();
            var pending = setting.Value;

            var participant = pending.Participants.Find(p => string.Equals(p.UserId, current.UserId, StringComparison.Ordinal));
            if (participant is null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(participant.AccountKey))
            {
                // Invite links remain reusable for the intended signer until the request expires,
                // but once a key is submitted the page is read-only and never overwrites it.
                current.AccountKey = participant.AccountKey;
                current.MasterFingerprint = participant.MasterFingerprint;
                current.AccountKeyPath = participant.AccountKeyPath;
                current.Submitted = true;
                TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Your signer key is submitted."].Value;
                return View("MultisigInvite", current);
            }

            var duplicateKeyFound = pending.Participants
                .Where(p => !string.Equals(p.UserId, current.UserId, StringComparison.Ordinal))
                .Any(p =>
                    multisigService.TryNormalizeAccountKeyForNetwork(p.AccountKey, network, out var normalizedParticipantKey) &&
                    string.Equals(normalizedParticipantKey, current.AccountKey, StringComparison.Ordinal));
            if (duplicateKeyFound)
            {
                ModelState.AddModelError(string.Empty, stringLocalizer["This signer key is already used in this multisig request."].Value);
                return View("MultisigInvite", current);
            }

            participant.AccountKey = current.AccountKey;
            participant.MasterFingerprint = current.MasterFingerprint;
            participant.AccountKeyPath = current.AccountKeyPath;
            participant.SubmittedAt = DateTimeOffset.UtcNow;
            if (!await storeRepository.TryUpdateSettingAsync(storeId, settingName, setting.XMin, pending))
                continue;

            await multisigNotificationService.NotifyRequesterOfSubmission(HttpContext, storeId, cryptoCode, pending, participant);
            current.Submitted = true;
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Signer key submitted successfully."].Value;
            return View("MultisigInvite", current);
        }

        ModelState.AddModelError(string.Empty, stringLocalizer["This multisig request changed while you were submitting your signer key. Please try again."].Value);
        return View("MultisigInvite", current);
    }

    private async Task<InviteLoadResult> LoadInviteViewModel(string storeId, string cryptoCode, string token)
    {
        if (!multisigService.TryReadInviteToken(token, out var payload) ||
            !string.Equals(payload.StoreId, storeId, StringComparison.Ordinal) ||
            !string.Equals(payload.CryptoCode, cryptoCode, StringComparison.OrdinalIgnoreCase))
            return new InviteLoadResult { Status = InviteLoadStatus.Invalid };
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(currentUserId, payload.UserId, StringComparison.Ordinal))
            return new InviteLoadResult { Status = InviteLoadStatus.WrongUser };
        if (!(await storeRepository.GetStoreUsers(storeId)).Any(u => string.Equals(u.Id, payload.UserId, StringComparison.Ordinal)))
            return new InviteLoadResult { Status = InviteLoadStatus.Invalid };

        var pending = await storeRepository.GetSettingAsync<PendingMultisigSetupData>(storeId, MultisigService.GetPendingMultisigSettingName(cryptoCode));
        if (pending is null || !string.Equals(pending.RequestId, payload.RequestId, StringComparison.Ordinal) ||
            pending.ExpiresAt < DateTimeOffset.UtcNow)
            return new InviteLoadResult { Status = InviteLoadStatus.Invalid };

        var participant = pending.Participants.Find(p => string.Equals(p.UserId, payload.UserId, StringComparison.Ordinal));
        if (participant is null)
            return new InviteLoadResult { Status = InviteLoadStatus.Invalid };

        return new InviteLoadResult
        {
            Status = InviteLoadStatus.Ok,
            ViewModel = new MultisigInviteViewModel
            {
                StoreId = storeId,
                CryptoCode = cryptoCode,
                Token = token,
                RequestId = pending.RequestId,
                UserId = participant.UserId,
                UserEmail = participant.Email,
                UserName = participant.Name,
                ExpiresAt = pending.ExpiresAt,
                RequiredSigners = pending.RequiredSigners,
                TotalSigners = pending.TotalSigners,
                ScriptType = pending.ScriptType,
                AccountKey = participant.AccountKey,
                MasterFingerprint = participant.MasterFingerprint,
                AccountKeyPath = participant.AccountKeyPath,
                Submitted = !string.IsNullOrWhiteSpace(participant.AccountKey)
            }
        };
    }
}
