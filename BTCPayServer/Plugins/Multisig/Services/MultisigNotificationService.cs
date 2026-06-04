#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Multisig.Events;
using BTCPayServer.Plugins.Multisig.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigNotificationService(
    EventAggregator eventAggregator,
    LinkGenerator linkGenerator,
    ApplicationDbContextFactory dbContextFactory,
    IEnumerable<EmailTriggerViewModel> emailTriggers)
{
    private readonly EmailTriggerViewModel[] _emailTriggers = emailTriggers.ToArray();

    public async Task EnsureDefaultEmailRules(string storeId, CancellationToken cancellationToken = default)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var existingTriggers = await ctx.EmailRules
            .Where(r => r.StoreId == storeId && MultisigEmailTriggers.DefaultRuleTriggers.Contains(r.Trigger))
            .Select(r => r.Trigger)
            .ToArrayAsync(cancellationToken);
        var existing = existingTriggers.ToHashSet(StringComparer.Ordinal);
        foreach (var triggerViewModel in _emailTriggers.Where(t => MultisigEmailTriggers.DefaultRuleTriggers.Contains(t.Trigger)))
        {
            if (triggerViewModel.DefaultEmail is null || existing.Contains(triggerViewModel.Trigger))
                continue;

            ctx.EmailRules.Add(new EmailRuleData
            {
                StoreId = storeId,
                Trigger = triggerViewModel.Trigger,
                To = triggerViewModel.DefaultEmail.To,
                CC = triggerViewModel.DefaultEmail.CC,
                BCC = triggerViewModel.DefaultEmail.BCC,
                Subject = triggerViewModel.DefaultEmail.Subject,
                Body = triggerViewModel.DefaultEmail.Body
            });
        }

        await ctx.SaveChangesAsync(cancellationToken);
    }

    public Task PublishSignerKeyRequestedEvents(string storeId, string cryptoCode, PendingMultisigSetupData pending)
    {
        var link = linkGenerator.CreateSessionLink(pending.RequestId, pending.RequestBaseUrl);
        foreach (var participant in pending.Participants.Where(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            eventAggregator.Publish(new MultisigSignerKeyRequestedEvent(
                storeId,
                cryptoCode,
                pending.RequestId,
                participant.Email,
                participant.Name,
                link));
        }

        return Task.CompletedTask;
    }

    public Task PublishSignerKeySubmittedEvent(string storeId, PendingMultisigSetupData pending, PendingMultisigSetupParticipantData participant)
    {
        var setupLink = linkGenerator.CreateSessionLink(pending.RequestId, pending.RequestBaseUrl);
        eventAggregator.Publish(new MultisigSignerKeySubmittedEvent(
            storeId,
            pending.CryptoCode,
            pending.RequestId,
            pending.RequestedByEmail,
            participant.Email,
            participant.Name,
            setupLink));
        return Task.CompletedTask;
    }

    public Task PublishWalletCreatedEvent(string storeId, string cryptoCode, PendingMultisigSetupData pending)
    {
        var walletId = new WalletId(storeId, cryptoCode);
        var walletLink = linkGenerator.WalletTransactionsLink(walletId, pending.RequestBaseUrl);

        var participantIds = pending.Participants
            .Select(p => p.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        eventAggregator.Publish(new MultisigWalletCreatedEvent(
            storeId,
            cryptoCode,
            pending.RequestId,
            walletLink,
            participantIds));
        return Task.CompletedTask;
    }
}
