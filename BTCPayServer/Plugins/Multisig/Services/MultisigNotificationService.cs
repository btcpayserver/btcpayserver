#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MimeKit;
using NBXplorer.Models;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigNotificationService(
    EmailSenderFactory emailSenderFactory,
    StoreRepository storeRepository,
    PermissionService permissionService,
    MultisigService multisigService,
    ILogger<MultisigNotificationService> logger)
{
    public async Task SendSignerRequestEmails(HttpContext httpContext, string storeId, string cryptoCode, PendingMultisigSetupData pending, IEnumerable<string>? participantIds = null)
    {
        if (!await emailSenderFactory.IsComplete(storeId))
            return;

        var sender = await emailSenderFactory.GetEmailSender(storeId);
        var allowedIds = participantIds?.ToHashSet(StringComparer.Ordinal);
        foreach (var participant in pending.Participants.Where(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            if (allowedIds is not null && !allowedIds.Contains(participant.UserId))
                continue;

            var link = multisigService.CreateInviteLink(httpContext, storeId, cryptoCode, pending.RequestId, participant.UserId, pending.ExpiresAt, absolute: true);
            if (string.IsNullOrEmpty(link))
                continue;

            TrySendEmail(
                sender,
                participant.Email,
                $"Multisig signer request for {cryptoCode}",
                $"A multisig wallet setup requires your account key.<br/>Open this link and submit your signer key:<br/><a href=\"{link}\">{link}</a>",
                storeId,
                participant.UserId);
        }
    }

    public async Task NotifyRequesterOfSubmission(HttpContext httpContext, string storeId, string cryptoCode, PendingMultisigSetupData pending, PendingMultisigSetupParticipantData participant)
    {
        if (!await emailSenderFactory.IsComplete(storeId))
            return;

        try
        {
            var sender = await emailSenderFactory.GetEmailSender(storeId);
            var recipientEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(pending.RequestedByEmail))
                recipientEmails.Add(pending.RequestedByEmail);

            var managerEmails = await GetWalletScopedRecipients(
                storeId,
                cryptoCode,
                allPolicies: new[]
                {
                    Policies.CanManageWalletSettings
                },
                requireWalletTypePolicy: true);
            foreach (var email in managerEmails)
                recipientEmails.Add(email);

            var setupLink = multisigService.CreateSetupLink(httpContext, storeId, cryptoCode, pending.RequestId, absolute: true);
            foreach (var recipient in recipientEmails)
            {
                TrySendEmail(
                    sender,
                    recipient,
                    $"Multisig signer submitted ({cryptoCode})",
                    $"<b>{participant.Name ?? participant.Email ?? participant.UserId}</b> submitted a signer key for request <span class=\"font-monospace\">{pending.RequestId}</span>.<br/>" +
                    (string.IsNullOrEmpty(setupLink) ? string.Empty : $"Open request: <a href=\"{setupLink}\">{setupLink}</a>"),
                    storeId,
                    participant.UserId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify requester about multisig submission for store {StoreId}", storeId);
        }
    }

    public async Task SendWalletCreatedEmails(HttpContext httpContext, string storeId, string cryptoCode, PendingMultisigSetupData pending)
    {
        if (!await emailSenderFactory.IsComplete(storeId))
            return;

        var sender = await emailSenderFactory.GetEmailSender(storeId);
        var walletId = new WalletId(storeId, cryptoCode).ToString();
        var walletLink = $"/wallets/{walletId}";
        if (httpContext.Request.Host.HasValue)
            walletLink = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{walletLink}";

        var participantIds = pending.Participants
            .Select(p => p.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var recipients = await GetWalletScopedRecipients(
            storeId,
            cryptoCode,
            anyPolicies: new[]
            {
                Policies.CanCreateWalletTransactions,
                Policies.CanManageWalletTransactions,
                Policies.CanViewWallet
            },
            includeUserIds: participantIds);

        foreach (var recipient in recipients)
        {
            TrySendEmail(
                sender,
                recipient,
                $"Multisig wallet created for {cryptoCode}",
                $"The multisig wallet setup is complete.<br/>Open wallet: <a href=\"{walletLink}\">{walletLink}</a>",
                storeId);
        }
    }

    public async Task NotifyPendingTransactionCreated(WalletId walletId, PendingTransaction pendingTransaction, DerivationSchemeSettings derivation)
    {
        if (!IsServerMultisig(walletId, derivation))
            return;
        if (!await emailSenderFactory.IsComplete(walletId.StoreId))
            return;

        var recipients = await GetWalletScopedRecipients(
            walletId.StoreId,
            walletId.CryptoCode,
            allPolicies: new[] { Policies.CanViewWallet },
            anyPolicies: new[] { Policies.CanSignWalletTransactions, Policies.CanManageWalletTransactions });
        if (recipients.Length == 0)
            return;

        var sender = await emailSenderFactory.GetEmailSender(walletId.StoreId);
        var pendingLink = GetPendingTransactionLink(walletId, pendingTransaction);
        foreach (var recipient in recipients)
        {
            TrySendEmail(
                sender,
                recipient,
                $"Pending multisig transaction requires signatures ({walletId.CryptoCode})",
                $"A pending multisig transaction was created and needs signatures.<br/><a href=\"{pendingLink}\">Open pending transaction</a>",
                walletId.StoreId);
        }
    }

    public async Task NotifyPendingTransactionSignatureCollected(WalletId walletId, PendingTransaction pendingTransaction, DerivationSchemeSettings derivation, string? signerUserId)
    {
        if (!IsServerMultisig(walletId, derivation))
            return;
        if (!await emailSenderFactory.IsComplete(walletId.StoreId))
            return;

        var recipients = await GetWalletScopedRecipients(
            walletId.StoreId,
            walletId.CryptoCode,
            allPolicies: new[] { Policies.CanViewWallet },
            anyPolicies: new[] { Policies.CanCreateWalletTransactions, Policies.CanManageWalletTransactions },
            excludeUserId: signerUserId);
        if (recipients.Length == 0)
            return;

        var sender = await emailSenderFactory.GetEmailSender(walletId.StoreId);
        var pendingLink = GetPendingTransactionLink(walletId, pendingTransaction);
        var blob = pendingTransaction.GetBlob();
        var progress = blob is null
            ? "Signature was collected."
            : $"Progress: <b>{blob.SignaturesCollected}/{blob.SignaturesNeeded ?? blob.SignaturesTotal ?? 0}</b> signatures.";
        foreach (var recipient in recipients)
        {
            TrySendEmail(
                sender,
                recipient,
                $"Multisig signature collected ({walletId.CryptoCode})",
                $"A signer submitted a signature for the pending multisig transaction.<br/>{progress}<br/><a href=\"{pendingLink}\">Open pending transaction</a>",
                walletId.StoreId);
        }
    }

    private static bool IsServerMultisig(WalletId walletId, DerivationSchemeSettings derivation)
    {
        return walletId?.StoreId is not null &&
               derivation?.AccountKeySettings is { Length: > 1 } &&
               derivation.IsMultiSigOnServer;
    }

    private string GetPendingTransactionLink(WalletId walletId, PendingTransaction pendingTransaction)
    {
        var path = $"/wallets/{walletId}/pending/{pendingTransaction.Id}";
        var blob = pendingTransaction.GetBlob();
        return blob?.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out var requestBaseUrl)
            ? requestBaseUrl.GetUrl(path)
            : path;
    }

    private async Task<string[]> GetWalletScopedRecipients(
        string storeId,
        string cryptoCode,
        IEnumerable<string>? allPolicies = null,
        IEnumerable<string>? anyPolicies = null,
        string? excludeUserId = null,
        IEnumerable<string>? includeUserIds = null,
        bool requireWalletTypePolicy = false)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(cryptoCode))
            return Array.Empty<string>();

        var requiredAll = (allPolicies ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiredAny = (anyPolicies ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var included = includeUserIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var walletTypePolicy = cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase)
            ? Policies.CanModifyBitcoinOnchain
            : Policies.CanModifyOtherWallets;

        var storeUsers = await storeRepository.GetStoreUsers(storeId);
        return storeUsers
            .Where(u => u is not null &&
                        !string.IsNullOrWhiteSpace(u.Id) &&
                        !string.IsNullOrWhiteSpace(u.Email))
            .Where(u => included is null || included.Contains(u.Id))
            .Where(u => excludeUserId is null || !string.Equals(u.Id, excludeUserId, StringComparison.Ordinal))
            .Where(u =>
            {
                var permissionSet = u.StoreRole?.ToPermissionSet(storeId) ?? new PermissionSet();
                if (requireWalletTypePolicy && !permissionSet.HasPermission(walletTypePolicy, storeId, permissionService))
                    return false;
                if (requiredAll.Any(policy => !permissionSet.HasPermission(policy, storeId, permissionService)))
                    return false;
                if (requiredAny.Length > 0 && !requiredAny.Any(policy => permissionSet.HasPermission(policy, storeId, permissionService)))
                    return false;
                return true;
            })
            .Select(u => u.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool TrySendEmail(IEmailSender sender, string recipient, string subject, string body, string storeId, string? userId = null)
    {
        if (!MailboxAddressValidator.TryParse(recipient, out var mailboxAddress))
        {
            logger.LogWarning(
                "Skipping multisig email for store {StoreId} and user {UserId}: invalid email '{Email}'",
                storeId,
                userId ?? string.Empty,
                recipient);
            return false;
        }

        try
        {
            sender.SendEmail(mailboxAddress, subject, body);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send multisig email for store {StoreId} and user {UserId}", storeId, userId ?? string.Empty);
            return false;
        }
    }
}
