using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class InvoiceWebhookProvider : WebhookProvider<InvoiceEvent>
{
    public InvoiceWebhookProvider(WebhookSender webhookSender, EventAggregator eventAggregator,
        ILogger<InvoiceWebhookProvider> logger) : base(
        eventAggregator, logger, webhookSender)
    {
    }

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>
        {
            {WebhookEventType.InvoiceCreated, "Invoice - Created"},
            {WebhookEventType.InvoiceReceivedPayment, "Invoice - Received Payment"},
            {WebhookEventType.InvoicePaymentSettled, "Invoice - Payment Settled"},
            {WebhookEventType.InvoiceProcessing, "Invoice - Is Processing"},
            {WebhookEventType.InvoiceExpired, "Invoice - Expired"},
            {WebhookEventType.InvoiceSettled, "Invoice - Is Settled"},
            {WebhookEventType.InvoiceInvalid, "Invoice - Became Invalid"},
        };
    }

    protected override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(InvoiceEvent invoiceEvent,
        WebhookData webhook)
    {
        var webhookEvent = GetWebhookEvent(invoiceEvent)!;
        var webhookBlob = webhook?.GetBlob();
        webhookEvent.InvoiceId = invoiceEvent.InvoiceId;
        webhookEvent.StoreId = invoiceEvent.Invoice.StoreId;
        webhookEvent.Metadata = invoiceEvent.Invoice.Metadata.ToJObject();
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        WebhookDeliveryData delivery = webhook is null? null: WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }
        return new InvoiceWebhookDeliveryRequest(invoiceEvent.Invoice, webhook?.Id, webhookEvent,
            delivery, webhookBlob);
    }

    public override WebhookEvent CreateTestEvent(string type, params object[] args)
    {
        var storeId = args[0].ToString();
        return new WebhookInvoiceEvent(type, storeId)
        {
            InvoiceId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    protected override WebhookInvoiceEvent GetWebhookEvent(InvoiceEvent invoiceEvent)
    {
        var eventCode = invoiceEvent.EventCode;
        var storeId = invoiceEvent.Invoice.StoreId;
        switch (eventCode)
        {
            case InvoiceEventCode.Confirmed:
            case InvoiceEventCode.MarkedCompleted:
                return new WebhookInvoiceSettledEvent(storeId)
                {
                    ManuallyMarked = eventCode == InvoiceEventCode.MarkedCompleted,
                    OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver
                };
            case InvoiceEventCode.Created:
                return new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated, storeId);
            case InvoiceEventCode.Expired:
                return new WebhookInvoiceExpiredEvent(storeId)
                {
                    PartiallyPaid = invoiceEvent.PaidPartial
                };
            case InvoiceEventCode.FailedToConfirm:
            case InvoiceEventCode.MarkedInvalid:
                return new WebhookInvoiceInvalidEvent(storeId)
                {
                    ManuallyMarked = eventCode == InvoiceEventCode.MarkedInvalid
                };
            case InvoiceEventCode.PaidInFull:
                return new WebhookInvoiceProcessingEvent(storeId)
                {
                    OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver
                };
            case InvoiceEventCode.ReceivedPayment:
                return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoiceReceivedPayment, storeId)
                {
                    AfterExpiration =
                        invoiceEvent.Invoice.Status == InvoiceStatus.Expired ||
                        invoiceEvent.Invoice.Status == InvoiceStatus.Invalid,
                    PaymentMethodId = invoiceEvent.Payment.PaymentMethodId.ToString(),
                    Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment),
                    StoreId = invoiceEvent.Invoice.StoreId
                };
            case InvoiceEventCode.PaymentSettled:
                return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoicePaymentSettled, storeId)
                {
                    AfterExpiration =
                        invoiceEvent.Invoice.Status == InvoiceStatus.Expired ||
                        invoiceEvent.Invoice.Status == InvoiceStatus.Invalid,
                    PaymentMethodId = invoiceEvent.Payment.PaymentMethodId.ToString(),
                    Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment),
                    StoreId = invoiceEvent.Invoice.StoreId
                };
            default:
                return null;
        }
    }
}
