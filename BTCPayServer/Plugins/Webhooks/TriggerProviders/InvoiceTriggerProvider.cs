#nullable  enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services.Invoices;
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
        AddInvoiceToModel(model, evt.Invoice, linkGenerator);
        return model;
    }

    public static void AddInvoiceToModel(JObject model, InvoiceEntity invoice, LinkGenerator linkGenerator)
    {
        var address = GetMailboxAddress(invoice.Metadata);
        model["Invoice"] = new JObject()
        {
            ["Id"] = invoice.Id,
            ["StoreId"] = invoice.StoreId,
            ["Price"] = invoice.Price,
            ["Currency"] = invoice.Currency,
            ["Status"] = invoice.Status.ToString(),
            ["AdditionalStatus"] = invoice.ExceptionStatus.ToString(),
            ["OrderId"] = invoice.Metadata.OrderId,
            ["Metadata"] = invoice.Metadata.ToJObject(),
            ["Link"] = linkGenerator.InvoiceLink(invoice.Id, invoice.GetRequestBaseUrl()),
            ["Buyer"] = new JObject()
            {
                ["Name"] = address?.Name ?? "",
                ["Email"] = address?.Address ?? "",
                ["MailboxAddress"] = address?.ToString() ?? ""
            }
        };
    }

    public static List<EmailTriggerViewModel.PlaceHolder> GetInvoicePlaceholders()
    => new()
    {
        new("{Invoice.Id}", "The id of the invoice"),
        new("{Invoice.StoreId}", "The id of the store"),
        new("{Invoice.Price}", "The price of the invoice"),
        new("{Invoice.Currency}", "The currency of the invoice"),
        new("{Invoice.Status}", "The current status of the invoice"),
        new("{Invoice.Link}", "The backend link to the invoice"),
        new("{Invoice.AdditionalStatus}", "Additional status information of the invoice"),
        new("{Invoice.OrderId}", "The order id associated with the invoice"),
        new("{Invoice.Buyer.Name}", "The name of the buyer taken from the invoice's metadata buyerName (eg. John Doe)"),
        new("{Invoice.Buyer.Email}", "The email of the buyer taken from the invoice's metadata buyerEmail (eg. john.doe@example.com)"),
        new("{Invoice.Buyer.MailboxAddress}", "The formatted mailbox address to use when sending an email to the buyer. (eg. \"John Doe\" <john.doe@example.com>)"),
        new("{Invoice.Metadata}*", "The metadata associated with the invoice")
    };

    public static MimeKit.MailboxAddress? GetMailboxAddress(InvoiceMetadata? invoiceMetadata)
    {
        if (invoiceMetadata?.AdditionalData is null)
            return null;
        var email = invoiceMetadata.BuyerEmail;
        var name = invoiceMetadata.BuyerName;
        if (email is not null)
            if (!MailboxAddressValidator.TryParse(email, out _))
                email = null;
        if (email is null)
            return null;
        try
        {
            return new MimeKit.MailboxAddress(name ?? "", email);
        }
        catch  // Invalid encoding or format; treat as no valid mailbox
        {
        }
        return null;
    }

    protected override Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext<InvoiceEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        if (GetMailboxAddress(evt.Invoice.Metadata) is {} mb &&
            context.MatchedRule.GetBTCPayAdditionalData()?.CustomerEmail is true)
        {
            context.To.Insert(0, mb);
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
