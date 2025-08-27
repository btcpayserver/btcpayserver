#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Mails;
using Microsoft.Extensions.Logging;
using MimeKit;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Emails;

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

    public List<MailboxAddress> Recipients { get; set; } = new();
    public List<MailboxAddress> Cc { get; set; } = new();
    public List<MailboxAddress> Bcc { get; set; } = new();
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
            var actionableRules = await ctx.EmailRules
                .GetMatches(triggEvent.StoreId, triggEvent.Trigger, triggEvent.Model);

            if (actionableRules.Length > 0)
            {
                var sender = await emailSenderFactory.GetEmailSender(triggEvent.StoreId);
                foreach (var actionableRule in actionableRules)
                {
                    var matchedContext = new EmailRuleMatchContext(triggEvent, actionableRule);

                    var body = new TextTemplate(actionableRule.Body ?? "");
                    var subject = new TextTemplate(actionableRule.Subject ?? "");
                    matchedContext.Recipients.AddRange(
                        actionableRule.To
                        .Select(o =>
                        {
                            if (!MailboxAddressValidator.TryParse(o, out var oo))
                            {
                                MailboxAddressValidator.TryParse(new TextTemplate(o).Render(triggEvent.Model), out oo);
                            }
                            return oo;
                        })
                        .Where(o => o != null)!);

                    if (triggEvent.Owner is not null)
                        await triggEvent.Owner.BeforeSending(matchedContext);
                    if (matchedContext.Recipients.Count == 0)
                        continue;
                    sender.SendEmail(matchedContext.Recipients.ToArray(), matchedContext.Cc.ToArray(), matchedContext.Bcc.ToArray(), subject.Render(triggEvent.Model), body.Render(triggEvent.Model));
                }
            }
        }
    }
}
