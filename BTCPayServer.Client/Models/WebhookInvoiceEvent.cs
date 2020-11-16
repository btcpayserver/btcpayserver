using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
        [JsonProperty(Order = 1)]
        public string StoreId { get; set; }
        [JsonProperty(Order = 2)]
        public string InvoiceId { get; set; }
    }

    public class WebhookInvoiceConfirmedEvent : WebhookInvoiceEvent
    {
        public WebhookInvoiceConfirmedEvent()
        {

        }
        public WebhookInvoiceConfirmedEvent(WebhookEventType evtType) : base(evtType)
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
    public class WebhookInvoicePaidEvent : WebhookInvoiceEvent
    {
        public WebhookInvoicePaidEvent()
        {

        }
        public WebhookInvoicePaidEvent(WebhookEventType evtType) : base(evtType)
        {
        }

        public bool OverPaid { get; set; }
        public bool PaidAfterExpiration { get; set; }
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
