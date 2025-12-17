#nullable enable
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Webhooks.TriggerProviders;

public class PaymentRequestTriggerProvider(LinkGenerator linkGenerator)
    : WebhookTriggerProvider<PaymentRequestEvent>
{
    protected override WebhookPaymentRequestEvent? GetWebhookEvent(PaymentRequestEvent evt)
    {
        var webhookEvt = evt.Type switch
        {
            PaymentRequestEvent.Created => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestCreated, evt.Data.StoreDataId),
            PaymentRequestEvent.Updated => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestUpdated, evt.Data.StoreDataId),
            PaymentRequestEvent.Archived => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestArchived, evt.Data.StoreDataId),
            PaymentRequestEvent.StatusChanged => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestStatusChanged, evt.Data.StoreDataId),
            PaymentRequestEvent.Completed => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestCompleted, evt.Data.StoreDataId),
            _ => null
        };
        if (webhookEvt is null)
            return null;
        webhookEvt.StoreId = evt.Data.StoreDataId;
        webhookEvt.PaymentRequestId = evt.Data.Id;
        webhookEvt.Status = evt.Data.Status;
        return webhookEvt;
    }

    protected override Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext<PaymentRequestEvent> webhookTriggerContext)
    {
        var email = webhookTriggerContext.Event.Data.GetBlob()?.Email;
        if (email != null &&
            context.MatchedRule.GetBTCPayAdditionalData()?.CustomerEmail is true &&
            MailboxAddressValidator.TryParse(email, out var mb))
        {
            context.To.Insert(0, mb);
        }
        return Task.CompletedTask;
    }

    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<PaymentRequestEvent> webhookTriggerContext)
    {
        var model = await base.GetEmailModel(webhookTriggerContext);
        var evt = webhookTriggerContext.Event;
        var data = evt.Data;
        var id = data.Id;
        var trimmedId = !string.IsNullOrEmpty(id) && id.Length > 15 ? $"{id.Substring(0, 7)}...{id.Substring(id.Length - 7)}" : id;
        var blob = data.GetBlob();
        var o = new JObject()
        {
            ["Id"] = id,
            ["TrimmedId"] = trimmedId,
            ["Amount"] = data.Amount.ToString(CultureInfo.InvariantCulture),
            ["Currency"] = data.Currency,
            ["Title"] = data.Title,
            ["Description"] = blob.Description,
            ["ReferenceId"] = data.ReferenceId,
            ["Status"] = evt.Data.Status.ToString(),
            ["FormResponse"] = blob.FormResponse,
        };
        model["PaymentRequest"] = o;

        if (blob.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out var v))
        {
            o["Link"] = linkGenerator.PaymentRequestLink(id, v);
        }

        return model;
    }
}
