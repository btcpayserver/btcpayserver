#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Multisig.Events;
using BTCPayServer.Plugins.Multisig.Models;
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

    public Task PublishSignerKeyRequestedEvents(MultisigSetupData pending)
    {
        foreach (var participant in pending.Participants.Where(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            eventAggregator.Publish(new MultisigSignerKeyRequestedEvent(
                pending,
                new(participant.Email,
                participant.Name)));
        }

        return Task.CompletedTask;
    }

    public void PublishSignerKeySubmittedEvent(MultisigSetupData pending, PendingMultisigSetupParticipantData participant)
    {
        eventAggregator.Publish(new MultisigSignerKeySubmittedEvent(
            pending,
            new(participant.Email,
            participant.Name)));
    }

    public void PublishWalletCreatedEvent(MultisigSetupData pending)
    {
        var walletLink = linkGenerator.WalletTransactionsLink(new WalletId(pending.StoreId, pending.CryptoCode), pending.RequestBaseUrl);
        var participantIds = pending.Participants
            .Select(p => p.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        eventAggregator.Publish(new MultisigWalletCreatedEvent(
            pending,
            walletLink,
            participantIds));
    }
}
