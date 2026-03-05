#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using NBitcoin;
using MimeKit;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIMultisigInviteController(
    StoreRepository storeRepo,
    IDataProtectionProvider dataProtectionProvider,
    ExplorerClientProvider explorerProvider,
    EmailSenderFactory emailSenderFactory,
    MultisigRecipientsService multisigRecipientsService,
    ILogger<UIMultisigInviteController> logger) : Controller
{
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

    private readonly IDataProtector _inviteProtector = dataProtectionProvider.CreateProtector("MultisigInviteLink");
    private readonly ExplorerClientProvider _explorerProvider = explorerProvider;
    private readonly EmailSenderFactory _emailSenderFactory = emailSenderFactory;
    private readonly MultisigRecipientsService _multisigRecipientsService = multisigRecipientsService;
    private readonly ILogger<UIMultisigInviteController> _logger = logger;

    [HttpGet("{storeId}/onchain/{cryptoCode}/multisig/invite/{**token}")]
    public async Task<IActionResult> SubmitMultisigSigner(string storeId, string cryptoCode, string token)
    {
        var result = await LoadInviteViewModel(storeId, cryptoCode, token);
        if (result.Status is InviteLoadStatus.WrongUser)
            return RedirectToAction("Login", "UIAccount", new { returnUrl = Request.Path + Request.QueryString });
        if (result.Status is not InviteLoadStatus.Ok)
            return NotFound();
        return View("~/Views/UIStoreOnChainWallets/ImportWallet/MultisigInvite.cshtml", result.ViewModel!);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/multisig/invite/{**token}")]
    public async Task<IActionResult> SubmitMultisigSigner(string storeId, string cryptoCode, string token, MultisigInviteViewModel vm)
    {
        var result = await LoadInviteViewModel(storeId, cryptoCode, token);
        if (result.Status is InviteLoadStatus.WrongUser)
            return RedirectToAction("Login", "UIAccount", new { returnUrl = Request.Path + Request.QueryString });
        if (result.Status is not InviteLoadStatus.Ok)
            return NotFound();
        var current = result.ViewModel!;

        var settingName = UIStoreOnChainWalletsController.GetPendingMultisigSettingName(cryptoCode);
        var pending = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, settingName);
        if (pending is null || !string.Equals(pending.RequestId, current.RequestId, StringComparison.Ordinal))
            return NotFound();

        var participant = pending.Participants.Find(p => string.Equals(p.UserId, current.UserId, StringComparison.Ordinal));
        if (participant is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(participant.AccountKey))
        {
            current.AccountKey = participant.AccountKey;
            current.MasterFingerprint = participant.MasterFingerprint;
            current.AccountKeyPath = participant.AccountKeyPath;
            current.Submitted = true;
            TempData[WellKnownTempData.SuccessMessage] = "Your signer key is submitted.";
            return View("~/Views/UIStoreOnChainWallets/ImportWallet/MultisigInvite.cshtml", current);
        }

        var network = _explorerProvider.GetNetwork(cryptoCode);
        if (network is null)
            return NotFound();

        current.AccountKey = vm.AccountKey?.Trim();
        current.MasterFingerprint = vm.MasterFingerprint?.Trim();
        current.AccountKeyPath = vm.AccountKeyPath?.Trim();

        if (string.IsNullOrWhiteSpace(current.AccountKey))
            ModelState.AddModelError(nameof(vm.AccountKey), "Please provide your account key.");
        else if (!UIStoreOnChainWalletsController.TryNormalizeAccountKeyForNetwork(current.AccountKey, network, out var normalizedAccountKey))
            ModelState.AddModelError(string.Empty, "Invalid account key format.");
        else
            current.AccountKey = normalizedAccountKey;
        if (!string.IsNullOrWhiteSpace(current.MasterFingerprint) && !Regex.IsMatch(current.MasterFingerprint, "^[0-9a-fA-F]{8}$"))
            ModelState.AddModelError(nameof(vm.MasterFingerprint), "Invalid fingerprint format.");
        if (!string.IsNullOrWhiteSpace(current.AccountKeyPath))
        {
            var normalizedPath = NormalizePath(current.AccountKeyPath);
            if (!KeyPath.TryParse(normalizedPath, out _))
                ModelState.AddModelError(nameof(vm.AccountKeyPath), "Invalid account key path.");
            else
                current.AccountKeyPath = $"m/{normalizedPath}";
        }

        if (ModelState.IsValid)
        {
            var duplicateKeyFound = pending.Participants
                .Where(p => !string.Equals(p.UserId, current.UserId, StringComparison.Ordinal))
                .Any(p =>
                    UIStoreOnChainWalletsController.TryNormalizeAccountKeyForNetwork(p.AccountKey, network, out var normalizedParticipantKey) &&
                    string.Equals(normalizedParticipantKey, current.AccountKey, StringComparison.Ordinal));
            if (duplicateKeyFound)
                ModelState.AddModelError(string.Empty, "This signer key is already used in this multisig request.");
        }

        if (!ModelState.IsValid)
            return View("~/Views/UIStoreOnChainWallets/ImportWallet/MultisigInvite.cshtml", current);

        participant.AccountKey = current.AccountKey;
        participant.MasterFingerprint = current.MasterFingerprint;
        participant.AccountKeyPath = current.AccountKeyPath;
        participant.SubmittedAt = DateTimeOffset.UtcNow;
        await storeRepo.UpdateSetting(storeId, settingName, pending);
        await NotifyRequesterOfSubmission(storeId, cryptoCode, pending, participant);

        current.Submitted = true;
        TempData[WellKnownTempData.SuccessMessage] = "Signer key submitted successfully.";
        return View("~/Views/UIStoreOnChainWallets/ImportWallet/MultisigInvite.cshtml", current);
    }

    private async Task<InviteLoadResult> LoadInviteViewModel(string storeId, string cryptoCode, string token)
    {
        if (!TryReadInviteToken(token, out var payload) ||
            !string.Equals(payload.StoreId, storeId, StringComparison.Ordinal) ||
            !string.Equals(payload.CryptoCode, cryptoCode, StringComparison.OrdinalIgnoreCase))
            return new InviteLoadResult { Status = InviteLoadStatus.Invalid };
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(currentUserId, payload.UserId, StringComparison.Ordinal))
            return new InviteLoadResult { Status = InviteLoadStatus.WrongUser };

        var pending = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, UIStoreOnChainWalletsController.GetPendingMultisigSettingName(cryptoCode));
        if (pending is null || !string.Equals(pending.RequestId, payload.RequestId, StringComparison.Ordinal) ||
            pending.ExpiresAt < DateTimeOffset.UtcNow || pending.Finalized)
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

    private static string NormalizePath(string path)
    {
        var normalizedPath = path
            .Replace("’", "'")
            .Replace("`", "'")
            .Replace("′", "'")
            .Replace(" ", string.Empty);
        normalizedPath = Regex.Replace(normalizedPath, @"([0-9]+)[hH]", "$1'");
        normalizedPath = normalizedPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase) ? normalizedPath[2..] : normalizedPath;
        return normalizedPath;
    }

    private bool TryReadInviteToken(string token, out (string StoreId, string CryptoCode, string RequestId, string UserId) value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        try
        {
            string protectedPayload;
            try
            {
                protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch
            {
                protectedPayload = token;
            }
            var raw = _inviteProtector.Unprotect(protectedPayload);
            var parts = raw.Split('|');
            if (parts.Length != 5)
                return false;
            if (!long.TryParse(parts[4], out var expires))
                return false;
            if (DateTimeOffset.FromUnixTimeSeconds(expires) < DateTimeOffset.UtcNow)
                return false;
            value = (parts[0], parts[1], parts[2], parts[3]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task NotifyRequesterOfSubmission(string storeId, string cryptoCode, PendingMultisigSetupData pending, PendingMultisigSetupParticipantData participant)
    {
        if (_emailSenderFactory is null)
            return;
        if (!await _emailSenderFactory.IsComplete(storeId))
            return;
        try
        {
            var sender = await _emailSenderFactory.GetEmailSender(storeId);
            var recipientEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(pending.RequestedByEmail))
            {
                recipientEmails.Add(pending.RequestedByEmail);
            }

            var managerEmails = await _multisigRecipientsService.GetWalletScopedRecipients(
                storeId,
                cryptoCode,
                anyPolicies: new[]
                {
                    Policies.CanManageWalletSettings,
                    Policies.CanManageWallets,
                    Policies.CanModifyStoreSettings
                });
            foreach (var email in managerEmails)
            {
                recipientEmails.Add(email);
            }

            if (recipientEmails.Count == 0)
                return;

            var submitted = pending.Participants.Count(p => !string.IsNullOrWhiteSpace(p.AccountKey));
            var total = pending.Participants.Count;
            var signerDisplay = !string.IsNullOrWhiteSpace(participant.Name) ? participant.Name : participant.Email;
            var setupLink = Url.Action(
                nameof(UIStoreOnChainWalletsController.ImportWallet),
                "UIStoreOnChainWallets",
                new { storeId, cryptoCode, method = "multisig", multisigRequestId = pending.RequestId },
                Request.Scheme);
            foreach (var recipientEmail in recipientEmails)
            {
                if (!MailboxAddressValidator.TryParse(recipientEmail, out var mailboxAddress))
                {
                    _logger.LogWarning(
                        "Skipping multisig signer submission email for store {StoreId}: invalid email '{Email}'",
                        storeId,
                        recipientEmail);
                    continue;
                }

                try
                {
                    sender.SendEmail(
                        mailboxAddress,
                        $"Multisig signer submitted ({cryptoCode})",
                        $"Signer <b>{signerDisplay}</b> submitted their account key.<br/>Progress: <b>{submitted}/{total}</b>.<br/><a href=\"{setupLink}\">Open multisig setup</a>");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to send multisig signer submission email for store {StoreId}",
                        storeId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to initialize multisig signer submission notifications for store {StoreId}",
                storeId);
        }
    }
}
