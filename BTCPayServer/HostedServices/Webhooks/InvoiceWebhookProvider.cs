using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class InvoiceWebhookDeliveryRequest : WebhookSender.WebhookDeliveryRequest
{
    public InvoiceEntity Invoice { get; }

    public InvoiceWebhookDeliveryRequest(InvoiceEntity invoice, string webhookId, WebhookEvent webhookEvent,
        WebhookDeliveryData delivery, WebhookBlob webhookBlob) : base(webhookId, webhookEvent, delivery, webhookBlob)
    {
        Invoice = invoice;
    }
}

public class InvoiceWebhookProvider : EventHostedServiceBase, IWebhookProvider
{
    private readonly WebhookSender _webhookSender;

    public InvoiceWebhookProvider(WebhookSender webhookSender, EventAggregator eventAggregator,
        ILogger<InvoiceWebhookProvider> logger) : base(
        eventAggregator, logger)
    {
        _webhookSender = webhookSender;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
        base.SubscribeToEvents();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            var webhooks = await _webhookSender.GetWebhooks(invoiceEvent.Invoice.StoreId);
            foreach (var webhook in webhooks)
            {
                var webhookBlob = webhook.GetBlob();
                if (GetWebhookEvent(invoiceEvent) is not { } webhookEvent)
                    continue;
                if (!webhookBlob.ShouldDeliver(webhookEvent.Type))
                    continue;
                WebhookDeliveryData delivery = WebhookExtensions.NewWebhookDelivery(webhook.Id);
                webhookEvent.InvoiceId = invoiceEvent.InvoiceId;
                webhookEvent.StoreId = invoiceEvent.Invoice.StoreId;
                webhookEvent.Metadata = invoiceEvent.Invoice.Metadata.ToJObject();
                webhookEvent.DeliveryId = delivery.Id;
                webhookEvent.WebhookId = webhook.Id;
                webhookEvent.OriginalDeliveryId = delivery.Id;
                webhookEvent.IsRedelivery = false;
                webhookEvent.Timestamp = delivery.Timestamp;
                var context = new InvoiceWebhookDeliveryRequest(invoiceEvent.Invoice, webhook.Id, webhookEvent,
                    delivery, webhookBlob);
                _webhookSender.EnqueueDelivery(context);
            }
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    public Dictionary<string, string> GetSupportedWebhookTypes()
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

    public WebhookEvent CreateTestEvent(string type, object[] args)
    {
        return new WebhookInvoiceEvent(type)
        {
            StoreId = args[0].ToString(), InvoiceId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    private static WebhookInvoiceEvent? GetWebhookEvent(InvoiceEvent invoiceEvent)
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
