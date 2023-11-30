﻿using System;
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
        return new Dictionary<string, string>()
        {
            {WebhookEventType.InvoiceCreated, "A new invoice has been created"},
            {WebhookEventType.InvoiceReceivedPayment, "A new payment has been received"},
            {WebhookEventType.InvoicePaymentSettled, "A payment has been settled"},
            {WebhookEventType.InvoiceProcessing, "An invoice is processing"},
            {WebhookEventType.InvoiceExpired, "An invoice has expired"},
            {WebhookEventType.InvoiceSettled, "An invoice has been settled"},
            {WebhookEventType.InvoiceInvalid, "An invoice became invalid"},
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
        return new WebhookInvoiceEvent(type)
        {
            StoreId = args[0].ToString(), InvoiceId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    protected override WebhookInvoiceEvent GetWebhookEvent(InvoiceEvent invoiceEvent)
    {
        var eventCode = invoiceEvent.EventCode;
        switch (eventCode)
        {
            case InvoiceEventCode.Completed:
            case InvoiceEventCode.PaidAfterExpiration:
                return null;
            case InvoiceEventCode.Confirmed:
            case InvoiceEventCode.MarkedCompleted:
                return new WebhookInvoiceSettledEvent()
                {
                    ManuallyMarked = eventCode == InvoiceEventCode.MarkedCompleted
                };
            case InvoiceEventCode.Created:
                return new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated);
            case InvoiceEventCode.Expired:
                return new WebhookInvoiceExpiredEvent() {PartiallyPaid = invoiceEvent.PaidPartial};
            case InvoiceEventCode.FailedToConfirm:
            case InvoiceEventCode.MarkedInvalid:
                return new WebhookInvoiceInvalidEvent() {ManuallyMarked = eventCode == InvoiceEventCode.MarkedInvalid};
            case InvoiceEventCode.PaidInFull:
                return new WebhookInvoiceProcessingEvent()
                {
                    OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver,
                };
            case InvoiceEventCode.ReceivedPayment:
                return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoiceReceivedPayment)
                {
                    AfterExpiration =
                        invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Expired ||
                        invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Invalid,
                    PaymentMethod = invoiceEvent.Payment.GetPaymentMethodId().ToStringNormalized(),
                    Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment)
                };
            case InvoiceEventCode.PaymentSettled:
                return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoicePaymentSettled)
                {
                    AfterExpiration =
                        invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Expired ||
                        invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Invalid,
                    PaymentMethod = invoiceEvent.Payment.GetPaymentMethodId().ToStringNormalized(),
                    Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment),
                    OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver,
                };
            default:
                return null;
        }
    }
}
