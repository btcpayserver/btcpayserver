#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Plugins.Multisig.Events;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Multisig.HostedServices;

public class MultisigEmailTriggerHostedService(
    EventAggregator eventAggregator,
    IServiceScopeFactory serviceScopeFactory,
    LinkGenerator linkGenerator,
    ILogger<MultisigEmailTriggerHostedService> logger)
    : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<MultisigSignerKeyRequestedEvent>();
        Subscribe<MultisigSignerKeySubmittedEvent>();
        Subscribe<MultisigWalletCreatedEvent>();
        Subscribe<PendingTransactionService.PendingTransactionEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case MultisigSignerKeyRequestedEvent signerKeyRequested:
                await PublishSignerKeyRequested(signerKeyRequested, cancellationToken);
                break;
            case MultisigSignerKeySubmittedEvent signerKeySubmitted:
                await PublishSignerKeySubmitted(signerKeySubmitted, cancellationToken);
                break;
            case MultisigWalletCreatedEvent walletCreated:
                await PublishWalletCreated(walletCreated, cancellationToken);
                break;
            case PendingTransactionService.PendingTransactionEvent pendingEvent:
                await PublishPendingTransactionEvent(pendingEvent, cancellationToken);
                break;
        }
    }

    private async Task PublishSignerKeyRequested(MultisigSignerKeyRequestedEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evt.Signer.Email))
            return;
        var model = new JObject();
        AddSetup(model, evt.Setup);
        AddSigner(model, evt.Signer);
        await PublishTrigger(evt.Setup.StoreId, MultisigEmailTriggers.SignerKeyRequested, model, cancellationToken);
    }

    private void AddSigner(JObject obj, MultisigSignerInfo info)
    {
        obj["Signer"] ??= new JObject();
        var signer = (JObject)obj["Signer"]!;
        signer["Email"] = info.Email;
        signer["Name"] = info.Name ?? info.Email ?? string.Empty;
        signer["MailboxAddress"] = GetMailboxAddress(info);
    }
    private void AddRecipient(JObject obj, RecipientInfo recipient)
    {
        obj["Recipient"] ??= new JObject();
        var signer = (JObject)obj["Recipient"]!;
        signer["Email"] = recipient.Email;
        signer["Name"] = recipient.Name ?? recipient.Email ?? string.Empty;
        signer["MailboxAddress"] = GetMailboxAddress(new(recipient.Email ?? "", recipient.Name ?? ""));
    }

    private string? GetMailboxAddress(MultisigSignerInfo info)
    {
        var name = info.Name;
        var email = info.Email;
        if (name == email)
            return email;
        try
        {
            return new MimeKit.MailboxAddress(name ?? "", email).ToString();
        }
        catch  // Invalid encoding or format; treat as no valid mailbox
        {
        }
        return email;
    }

    private async Task PublishSignerKeySubmitted(MultisigSignerKeySubmittedEvent evt, CancellationToken cancellationToken)
    {

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();

        var recipients = await GetWalletScopedRecipients(
            storeRepository,
            permissionService,
            evt.Setup.StoreId,
            allPolicies: new[] { WalletPolicies.CanManageWalletSettings });

        foreach (var recipient in recipients)
        {
            var model = new JObject();
            AddSetup(model, evt.Setup);
            AddRecipient(model, recipient);
            AddSigner(model, evt.Signer);
            await PublishTrigger(evt.Setup.StoreId, MultisigEmailTriggers.SignerKeySubmitted, model, cancellationToken);
        }
    }

    private void AddSetup(JObject model, MultisigSetupData setup)
    {
        model["CryptoCode"] = setup.CryptoCode;
        model["Setup"] ??= new JObject();
        var req = (JObject)model["Setup"]!;
        req["Id"] = setup.RequestId;
        req["Link"] = linkGenerator.MultisigSetupSessionLink(setup.RequestId, setup.RequestBaseUrl);
    }

    private async Task PublishWalletCreated(MultisigWalletCreatedEvent evt, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        var recipients = await GetWalletScopedRecipients(
            storeRepository,
            permissionService,
            evt.Setup.StoreId,
            includeUserIds: evt.ParticipantUserIds);

        foreach (var recipient in recipients)
        {
            var model = new JObject
            {
                ["Wallet"] = new JObject
                {
                    ["Link"] = evt.WalletLink
                }
            };
            AddSetup(model, evt.Setup);
            AddRecipient(model, recipient);
            await PublishTrigger(evt.Setup.StoreId, MultisigEmailTriggers.WalletCreated, model, cancellationToken);
        }
    }

    private async Task PublishPendingTransactionEvent(PendingTransactionService.PendingTransactionEvent pendingEvent, CancellationToken cancellationToken)
    {
        if (pendingEvent.Type is not PendingTransactionService.PendingTransactionEvent.Created and
            not PendingTransactionService.PendingTransactionEvent.SignatureCollected)
            return;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();

        var pendingTransaction = pendingEvent.Data;
        var store = await storeRepository.FindStore(pendingTransaction.StoreId);
        if (store is null)
            return;

        var walletId = new WalletId(pendingTransaction.StoreId, pendingTransaction.CryptoCode);
        var derivation = store.GetDerivationSchemeSettings(handlers, walletId.CryptoCode);
        if (derivation is null || !IsServerMultisig(derivation))
            return;

        var recipients = pendingEvent.Type is PendingTransactionService.PendingTransactionEvent.Created
            ? await GetWalletScopedRecipients(
                storeRepository,
                permissionService,
                walletId.StoreId,
                allPolicies: new[] { WalletPolicies.CanViewWallet },
                anyPolicies: new[] { WalletPolicies.CanSignWalletTransactions, WalletPolicies.CanManageWalletTransactions })
            : await GetWalletScopedRecipients(
                storeRepository,
                permissionService,
                walletId.StoreId,
                allPolicies: new[] { WalletPolicies.CanViewWallet },
                anyPolicies: new[]
                {
                    WalletPolicies.CanSignWalletTransactions,
                    WalletPolicies.CanCreateWalletTransactions,
                    WalletPolicies.CanManageWalletTransactions
                },
                excludeUserId: pendingEvent.SignerUserId);
        if (recipients.Length == 0)
            return;

        var blob = pendingTransaction.GetBlob();
        var signaturesCollected = blob?.SignaturesCollected ?? 0;
        var signaturesNeeded = blob?.SignaturesNeeded ?? blob?.SignaturesTotal ?? 0;
        var pendingTransactionLink = GetPendingTransactionLink(walletId, pendingTransaction);
        var trigger = pendingEvent.Type is PendingTransactionService.PendingTransactionEvent.Created
            ? MultisigEmailTriggers.PendingTransactionCreated
            : MultisigEmailTriggers.PendingTransactionSignatureCollected;

        foreach (var recipient in recipients)
        {
            var model = new JObject
            {
                ["CryptoCode"] = walletId.CryptoCode,
                ["PendingTransaction"] = new JObject
                {
                    ["Link"] = pendingTransactionLink,
                    ["SignaturesCollected"] = signaturesCollected,
                    ["SignaturesNeeded"] = signaturesNeeded,
                    ["SignaturesMissing"] = Math.Max(0, signaturesNeeded - signaturesCollected)
                }
            };
            AddRecipient(model, recipient);
            await PublishTrigger(walletId.StoreId, trigger, model, cancellationToken);
        }
    }

    private Task PublishTrigger(string storeId, string trigger, JObject model, CancellationToken cancellationToken)
    {
        EventAggregator.Publish(new TriggerEvent(storeId, trigger, model, null));
        return Task.CompletedTask;
    }

    private static bool IsServerMultisig(DerivationSchemeSettings derivation)
    {
        return derivation is { AccountKeySettings.Length: > 1, IsMultiSigOnServer: true };
    }

    private string? GetPendingTransactionLink(WalletId walletId, PendingTransaction pendingTransaction)
    {
        var blob = pendingTransaction.GetBlob();
        if (blob?.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out var requestBaseUrl))
            return linkGenerator.WalletPendingTransactionLink(walletId, pendingTransaction.Id, requestBaseUrl);
        return null;
    }

    record RecipientInfo(string Email, string Name);
    private static async Task<RecipientInfo[]> GetWalletScopedRecipients(
        StoreRepository storeRepository,
        PermissionService permissionService,
        string storeId,
        IEnumerable<string>? allPolicies = null,
        IEnumerable<string>? anyPolicies = null,
        string? excludeUserId = null,
        IEnumerable<string>? includeUserIds = null)
    {
        if (string.IsNullOrWhiteSpace(storeId))
            return Array.Empty<RecipientInfo>();

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
        bool HasPolicy(PermissionSet permissionSet, string policy)
        {
            return Permission.TryCreatePermission(policy, storeId, out var permission) &&
                   permissionSet.HasPermission(permission, permissionService);
        }

        var storeUsers = await storeRepository.GetStoreUsers(storeId);
        return storeUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.Id) &&
                        !string.IsNullOrWhiteSpace(u.Email))
            .Where(u => included is null || included.Contains(u.Id))
            .Where(u => excludeUserId is null || !string.Equals(u.Id, excludeUserId, StringComparison.Ordinal))
            .Where(u =>
            {
                var permissionSet = u.StoreRole?.ToPermissionSet(storeId) ?? new PermissionSet();
                if (!HasPolicy(permissionSet, WalletPolicies.CanViewWallet))
                    return false;
                if (requiredAll.Any(policy => !HasPolicy(permissionSet, policy)))
                    return false;
                return requiredAny.Length <= 0 || requiredAny.Any(policy => HasPolicy(permissionSet, policy));
            })
            .Select(u => new RecipientInfo(u.Email, u.UserBlob.Name))
            .DistinctBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
