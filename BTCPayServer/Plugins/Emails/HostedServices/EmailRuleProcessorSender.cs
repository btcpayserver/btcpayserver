#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.Services;
using Microsoft.Extensions.Logging;
using MimeKit;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Emails.HostedServices;

public interface ITriggerOwner
{
    Task BeforeSending(EmailRuleMatchContext context);
}

public record TriggerEvent(string? StoreId, string Trigger, JObject Model, ITriggerOwner? Owner)
{
    public override string ToString()
        => $"Trigger event '{Trigger}'";
}

public class EmailRuleMatchContext(
    TriggerEvent triggerEvent,
    EmailRuleData matchedRule)
{
    public TriggerEvent TriggerEvent { get; } = triggerEvent;
    public EmailRuleData MatchedRule { get; } = matchedRule;

    public List<MailboxAddress> To { get; set; } = new();
    public List<MailboxAddress> CC { get; set; } = new();
    public List<MailboxAddress> BCC { get; set; } = new();
}

public class StoreEmailRuleProcessorSender(
    ApplicationDbContextFactory dbContextFactory,
    EventAggregator eventAggregator,
    ILogger<StoreEmailRuleProcessorSender> logger,
    EmailSenderFactory emailSenderFactory)
    : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<TriggerEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is TriggerEvent triggEvent)
        {
            await using var ctx = dbContextFactory.CreateContext();
            var actionableRules = await ctx.EmailRules.GetMatches(triggEvent.StoreId, triggEvent.Trigger, triggEvent.Model);

            if (actionableRules.Length > 0)
            {
                var sender = await emailSenderFactory.GetEmailSender(triggEvent.StoreId);
                foreach (var actionableRule in actionableRules)
                {
                    var matchedContext = new EmailRuleMatchContext(triggEvent, actionableRule);

                    var body = new TextTemplate(actionableRule.Body ?? "");
                    var subject = new TextTemplate(actionableRule.Subject ?? "");
                    AddToMatchedContext(triggEvent.Model, matchedContext.To, actionableRule.To);
                    AddToMatchedContext(triggEvent.Model, matchedContext.CC, actionableRule.CC);
                    AddToMatchedContext(triggEvent.Model, matchedContext.BCC, actionableRule.BCC);

                    if (triggEvent.Owner is not null)
                        await triggEvent.Owner.BeforeSending(matchedContext);
                    if (matchedContext.To.Count == 0)
                        continue;
                    sender.SendEmail(matchedContext.To.ToArray(), matchedContext.CC.ToArray(), matchedContext.BCC.ToArray(), subject.Render(triggEvent.Model), body.Render(triggEvent.Model));
                }
            }
        }
    }

    private void AddToMatchedContext(JObject model, List<MailboxAddress> mailboxAddresses, string[] rulesAddresses)
    {
        mailboxAddresses.AddRange(
            rulesAddresses
                .SelectMany(o =>
                {
                    var emails = new TextTemplate(o).Render(model);
                    MailAddressCollection mailCollection = new();
                    try
                    {
                        mailCollection.Add(emails);
                    }
                    catch (FormatException)
                    {
                        return Array.Empty<MailboxAddress>();
                    }

                    return mailCollection.Select(a =>
                        {
                            MailboxAddressValidator.TryParse(a.ToString(), out var oo);
                            return oo;
                        })
                        .Where(a => a != null)
                        .ToArray();
                })
                .Where(o => o != null)!);
    }
}
