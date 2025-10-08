using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class WebhookPayoutEvent : StoreWebhookEvent
    {
        public WebhookPayoutEvent(string type, string storeId)
        {
            if (!type.StartsWith("payout", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(type));
            Type = type;
            StoreId = storeId;
        }

        [JsonProperty(Order = 2)] public string PayoutId { get; set; }
        [JsonProperty(Order = 3)] public string PullPaymentId { get; set; }
        [JsonProperty(Order = 4)] [JsonConverter(typeof(StringEnumConverter))]public PayoutState PayoutState { get; set; }
    }
    public class WebhookPaymentRequestEvent : StoreWebhookEvent
    {
        public WebhookPaymentRequestEvent(string type, string storeId)
        {
            if (!type.StartsWith("paymentrequest", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(type));
            Type = type;
            StoreId = storeId;
        }

        [JsonProperty(Order = 2)] public string PaymentRequestId { get; set; }
        [JsonProperty(Order = 3)] [JsonConverter(typeof(StringEnumConverter))]public PaymentRequestStatus Status { get; set; }
    }

    public abstract class StoreWebhookEvent : WebhookEvent
    {
        [JsonProperty(Order = 1)] public string StoreId { get; set; }
    }

    public class WebhookInvoiceEvent : StoreWebhookEvent
    {
        public WebhookInvoiceEvent()
        {
        }

        public WebhookInvoiceEvent(string evtType, string storeId)
        {
            if (!evtType.StartsWith("invoice", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(evtType));
            Type = evtType;
            StoreId = storeId;
        }

        [JsonProperty(Order = 2)] public string InvoiceId { get; set; }
        [JsonProperty(Order = 3)] public JObject Metadata { get; set; }
    }

    public class WebhookInvoiceSettledEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceSettledEvent(string storeId) : base(WebhookEventType.InvoiceSettled, storeId)
        {
        }

        public bool ManuallyMarked { get; set; }
        public bool OverPaid { get; set; }
    }

    public class WebhookInvoiceInvalidEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceInvalidEvent(string storeId) : base(WebhookEventType.InvoiceInvalid, storeId)
        {
        }

        public bool ManuallyMarked { get; set; }
    }

    public class WebhookInvoiceProcessingEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceProcessingEvent(string storeId) : base(WebhookEventType.InvoiceProcessing, storeId)
        {
        }

        public bool OverPaid { get; set; }
    }

    public class WebhookInvoiceReceivedPaymentEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceReceivedPaymentEvent(string type, string storeId) : base(type, storeId)
        {
        }

        public bool AfterExpiration { get; set; }
        public string PaymentMethodId { get; set; }
        public InvoicePaymentMethodDataModel.Payment Payment { get; set; }
    }

    public class WebhookInvoicePaymentSettledEvent : WebhookInvoiceReceivedPaymentEvent
    {
        public WebhookInvoicePaymentSettledEvent(string storeId) : base(WebhookEventType.InvoicePaymentSettled, storeId)
        {
        }
    }

    public class WebhookInvoiceExpiredEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceExpiredEvent(string storeId) : base(WebhookEventType.InvoiceExpired, storeId)
        {
        }

        public bool PartiallyPaid { get; set; }
    }
}
