using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class WebhookInvoiceEvent : WebhookEvent
    {
        public WebhookInvoiceEvent()
        {
        }

        public WebhookInvoiceEvent(WebhookEventType evtType)
        {
            this.Type = evtType;
        }

        [JsonProperty(Order = 1)] public string StoreId { get; set; }
        [JsonProperty(Order = 2)] public string InvoiceId { get; set; }
        [JsonProperty(Order = 3)] public JObject Metadata { get; set; }
    }

    public class WebhookInvoiceSettledEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceSettledEvent()
        {
        }

        public WebhookInvoiceSettledEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool ManuallyMarked { get; set; }
    }

    public class WebhookInvoiceInvalidEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceInvalidEvent()
        {
        }

        public WebhookInvoiceInvalidEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool ManuallyMarked { get; set; }
    }

    public class WebhookInvoiceProcessingEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceProcessingEvent()
        {
        }

        public WebhookInvoiceProcessingEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool OverPaid { get; set; }
    }

    public class WebhookInvoiceReceivedPaymentEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceReceivedPaymentEvent()
        {
        }

        public WebhookInvoiceReceivedPaymentEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool AfterExpiration { get; set; }
        public string PaymentMethod { get; set; }
        public InvoicePaymentMethodDataModel.Payment Payment { get; set; }
        public bool OverPaid { get; set; }
    }

    public class WebhookInvoicePaymentSettledEvent : WebhookInvoiceReceivedPaymentEvent
    {
        public WebhookInvoicePaymentSettledEvent()
        {
        }

        public WebhookInvoicePaymentSettledEvent(WebhookEventType evtType) : base(evtType)
        {
        }
    }

    public class WebhookInvoiceExpiredEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceExpiredEvent()
        {
        }

        public WebhookInvoiceExpiredEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool PartiallyPaid { get; set; }
    }
}
