#nullable  enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Webhooks.TriggerProviders;

public class InvoiceTriggerProvider(LinkGenerator linkGenerator)
    : WebhookTriggerProvider<InvoiceEvent>
{
    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<InvoiceEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var model = await base.GetEmailModel(webhookTriggerContext);
        // Keep for backward compatibility
        model["Invoice"] = new JObject()
        {
            ["Id"] = evt.Invoice.Id,
            ["StoreId"] = evt.Invoice.StoreId,
            ["Price"] = evt.Invoice.Price,
            ["Currency"] = evt.Invoice.Currency,
            ["Status"] = evt.Invoice.Status.ToString(),
            ["AdditionalStatus"] = evt.Invoice.ExceptionStatus.ToString(),
            ["OrderId"] = evt.Invoice.Metadata.OrderId,
            ["Metadata"] = evt.Invoice.Metadata.ToJObject(),
            ["Link"] = linkGenerator.InvoiceLink(evt.InvoiceId, evt.Invoice.GetRequestBaseUrl())
        };
        return model;
    }

    protected override Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext<InvoiceEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var email = evt.Invoice.Metadata?.BuyerEmail;
        if (email != null &&
            context.MatchedRule.GetBTCPayAdditionalData()?.CustomerEmail is true &&
            MailboxAddressValidator.TryParse(email, out var mb))
        {
            context.Recipients.Insert(0, mb);
        }
        return Task.CompletedTask;
    }

    protected override WebhookInvoiceEvent? GetWebhookEvent(InvoiceEvent invoiceEvent)
    {
        var eventCode = invoiceEvent.EventCode;
        var storeId = invoiceEvent.Invoice.StoreId;
        var evt = eventCode switch
        {
            InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted => new WebhookInvoiceSettledEvent(storeId)
            {
                ManuallyMarked = eventCode == InvoiceEventCode.MarkedCompleted,
                OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver
            },
            InvoiceEventCode.Created => new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated, storeId),
            InvoiceEventCode.Expired => new WebhookInvoiceExpiredEvent(storeId) { PartiallyPaid = invoiceEvent.PaidPartial },
            InvoiceEventCode.FailedToConfirm or InvoiceEventCode.MarkedInvalid => new WebhookInvoiceInvalidEvent(storeId) { ManuallyMarked = eventCode == InvoiceEventCode.MarkedInvalid },
            InvoiceEventCode.PaidInFull => new WebhookInvoiceProcessingEvent(storeId) { OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver },
            InvoiceEventCode.ReceivedPayment => new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoiceReceivedPayment, storeId)
            {
                AfterExpiration =
                    invoiceEvent.Invoice.Status == InvoiceStatus.Expired ||
                    invoiceEvent.Invoice.Status == InvoiceStatus.Invalid,
                PaymentMethodId = invoiceEvent.Payment.PaymentMethodId.ToString(),
                Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment)
            },
            InvoiceEventCode.PaymentSettled => new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoicePaymentSettled, storeId)
            {
                AfterExpiration =
                    invoiceEvent.Invoice.Status == InvoiceStatus.Expired ||
                    invoiceEvent.Invoice.Status == InvoiceStatus.Invalid,
                PaymentMethodId = invoiceEvent.Payment.PaymentMethodId.ToString(),
                Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment)
            },
            InvoiceEventCode.ExpiredPaidPartial => new WebhookInvoiceEvent(WebhookEventType.InvoiceExpiredPaidPartial, storeId),
            InvoiceEventCode.PaidAfterExpiration => new WebhookInvoiceEvent(WebhookEventType.InvoicePaidAfterExpiration, storeId),
            _ => null
        };
        if (evt is null)
            return null;
        evt.StoreId = storeId;
        evt.InvoiceId = invoiceEvent.InvoiceId;
        evt.Metadata = invoiceEvent.Invoice.Metadata.ToJObject();
        return evt;
    }
}
