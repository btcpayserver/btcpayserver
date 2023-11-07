using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class WebhookPayoutEvent:WebhookEvent
    {
        public WebhookPayoutEvent()
        {
        }

        public WebhookPayoutEvent(string evtType)
        {
            if(!evtType.StartsWith("payout", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(evtType));
            Type = evtType;
        }

        [JsonProperty(Order = 1)] public string StoreId { get; set; }
        [JsonProperty(Order = 2)] public string PayoutId { get; set; }
        [JsonProperty(Order = 3)] public string PullPaymentId { get; set; }
        [JsonProperty(Order = 4)] [JsonConverter(typeof(StringEnumConverter))]public PayoutState PayoutState { get; set; }
    } 
    public class WebhookPaymentRequestEvent:WebhookEvent
    {
        public WebhookPaymentRequestEvent()
        {
        }

        public WebhookPaymentRequestEvent(string evtType)
        {
            if(!evtType.StartsWith("paymentrequest", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(evtType));
            Type = evtType;
        }

        [JsonProperty(Order = 1)] public string StoreId { get; set; }
        [JsonProperty(Order = 2)] public string PaymentRequestId { get; set; }
        [JsonProperty(Order = 3)] [JsonConverter(typeof(StringEnumConverter))]public PaymentRequestData.PaymentRequestStatus Status { get; set; }
    }
    
    public class WebhookInvoiceEvent : WebhookEvent
    {
        public WebhookInvoiceEvent()
        {
        }

        public WebhookInvoiceEvent(string evtType)
        { 
            if(!evtType.StartsWith("invoice", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(evtType));
            Type = evtType;
        }

        [JsonProperty(Order = 1)] public string StoreId { get; set; }
        [JsonProperty(Order = 2)] public string InvoiceId { get; set; }
        [JsonProperty(Order = 3)] public JObject Metadata { get; set; }
    }

    public class WebhookInvoiceSettledEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceSettledEvent() : base(WebhookEventType.InvoiceSettled)
        {
        }

        public bool ManuallyMarked { get; set; }
    }

    public class WebhookInvoiceInvalidEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceInvalidEvent():base(WebhookEventType.InvoiceInvalid)
        {
        }

        public bool ManuallyMarked { get; set; }
    }

    public class WebhookInvoiceProcessingEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceProcessingEvent():base(WebhookEventType.InvoiceProcessing)
        {
        }

        public bool OverPaid { get; set; }
    }

    public class WebhookInvoiceReceivedPaymentEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceReceivedPaymentEvent() : base(WebhookEventType.InvoiceReceivedPayment)
        {
        }

        public WebhookInvoiceReceivedPaymentEvent(string type):base(type)
        {
        }

        public bool AfterExpiration { get; set; }
        public string PaymentMethod { get; set; }
        public InvoicePaymentMethodDataModel.Payment Payment { get; set; }
        public bool OverPaid { get; set; }
    }

    public class WebhookInvoicePaymentSettledEvent : WebhookInvoiceReceivedPaymentEvent
    {

        public WebhookInvoicePaymentSettledEvent() : base(WebhookEventType.InvoicePaymentSettled)
        {
        }
    }

    public class WebhookInvoiceExpiredEvent : WebhookInvoiceEvent
    {

        public WebhookInvoiceExpiredEvent() : base(WebhookEventType.InvoiceExpired)
        {
        }

        public bool PartiallyPaid { get; set; }
    }
}
