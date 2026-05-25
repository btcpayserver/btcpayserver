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
        if (string.IsNullOrWhiteSpace(evt.SignerEmail))
            return;

        await PublishTrigger(evt.StoreId, MultisigEmailTriggers.SignerKeyRequested, new JObject
        {
            ["CryptoCode"] = evt.CryptoCode,
            ["Request"] = new JObject
            {
                ["Id"] = evt.RequestId
            },
            ["Signer"] = new JObject
            {
                ["UserId"] = evt.SignerUserId,
                ["Email"] = evt.SignerEmail,
                ["Name"] = evt.SignerName ?? evt.SignerEmail,
                ["Link"] = evt.SignerLink
            }
        }, cancellationToken);
    }

    private async Task PublishSignerKeySubmitted(MultisigSignerKeySubmittedEvent evt, CancellationToken cancellationToken)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(evt.RequestedByEmail))
            recipients.Add(evt.RequestedByEmail);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        foreach (var recipient in await GetWalletScopedRecipients(
                     storeRepository,
                     permissionService,
                     evt.StoreId,
                     evt.CryptoCode,
                     allPolicies: new[] { WalletPolicies.CanManageWalletSettings }))
        {
            recipients.Add(recipient.Email);
        }

        foreach (var recipient in recipients)
        {
            await PublishTrigger(evt.StoreId, MultisigEmailTriggers.SignerKeySubmitted, new JObject
            {
                ["CryptoCode"] = evt.CryptoCode,
                ["Request"] = new JObject
                {
                    ["Id"] = evt.RequestId,
                    ["Link"] = evt.SetupLink
                },
                ["Signer"] = new JObject
                {
                    ["UserId"] = evt.SignerUserId,
                    ["Email"] = evt.SignerEmail,
                    ["Name"] = evt.SignerName ?? evt.SignerEmail ?? evt.SignerUserId ?? string.Empty
                },
                ["Recipient"] = new JObject
                {
                    ["Email"] = recipient
                }
            }, cancellationToken);
        }
    }

    private async Task PublishWalletCreated(MultisigWalletCreatedEvent evt, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        var recipients = await GetWalletScopedRecipients(
            storeRepository,
            permissionService,
            evt.StoreId,
            evt.CryptoCode,
            anyPolicies: new[]
            {
                WalletPolicies.CanCreateWalletTransactions,
                WalletPolicies.CanManageWalletTransactions,
                WalletPolicies.CanViewWallet
            },
            includeUserIds: evt.ParticipantUserIds);

        foreach (var recipient in recipients)
        {
            await PublishTrigger(evt.StoreId, MultisigEmailTriggers.WalletCreated, new JObject
            {
                ["CryptoCode"] = evt.CryptoCode,
                ["Request"] = new JObject
                {
                    ["Id"] = evt.RequestId
                },
                ["Wallet"] = new JObject
                {
                    ["Link"] = evt.WalletLink
                },
                ["Recipient"] = new JObject
                {
                    ["Email"] = recipient.Email
                }
            }, cancellationToken);
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
                walletId.CryptoCode,
                allPolicies: new[] { WalletPolicies.CanViewWallet },
                anyPolicies: new[] { WalletPolicies.CanSignWalletTransactions, WalletPolicies.CanManageWalletTransactions })
            : await GetWalletScopedRecipients(
                storeRepository,
                permissionService,
                walletId.StoreId,
                walletId.CryptoCode,
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
            await PublishTrigger(walletId.StoreId, trigger, new JObject
            {
                ["CryptoCode"] = walletId.CryptoCode,
                ["PendingTransaction"] = new JObject
                {
                    ["Link"] = pendingTransactionLink,
                    ["SignaturesCollected"] = signaturesCollected,
                    ["SignaturesNeeded"] = signaturesNeeded,
                    ["SignaturesMissing"] = Math.Max(0, signaturesNeeded - signaturesCollected)
                },
                ["Recipient"] = new JObject
                {
                    ["Email"] = recipient.Email
                }
            }, cancellationToken);
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

    private string GetPendingTransactionLink(WalletId walletId, PendingTransaction pendingTransaction)
    {
        var blob = pendingTransaction.GetBlob();
        if (blob?.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out var requestBaseUrl))
            return linkGenerator.WalletPendingTransactionLink(walletId, pendingTransaction.Id, requestBaseUrl);

        return linkGenerator.GetPathByAction(
                   nameof(UIWalletsController.ViewPendingTransaction),
                   "UIWallets",
                   new { area = WalletsPlugin.Area, walletId = walletId.ToString(), pendingTransactionId = pendingTransaction.Id }) ??
               $"/wallets/{walletId}/pending/{pendingTransaction.Id}";
    }

    private static async Task<Recipient[]> GetWalletScopedRecipients(
        StoreRepository storeRepository,
        PermissionService permissionService,
        string storeId,
        string cryptoCode,
        IEnumerable<string>? allPolicies = null,
        IEnumerable<string>? anyPolicies = null,
        string? excludeUserId = null,
        IEnumerable<string>? includeUserIds = null,
        bool requireWalletTypePolicy = true)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(cryptoCode))
            return Array.Empty<Recipient>();

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
                if (requireWalletTypePolicy && !HasPolicy(permissionSet, WalletPolicies.CanViewWallet))
                    return false;
                if (requiredAll.Any(policy => !HasPolicy(permissionSet, policy)))
                    return false;
                return requiredAny.Length <= 0 || requiredAny.Any(policy => HasPolicy(permissionSet, policy));
            })
            .Select(u => new Recipient(u.Id, u.Email))
            .DistinctBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private record Recipient(string UserId, string Email);
}
