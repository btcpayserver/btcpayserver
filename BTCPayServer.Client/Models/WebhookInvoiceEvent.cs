using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class WebhookInvoiceEvent : WebhookEvent
    {
        [JsonProperty(Order = 1)]
        public string StoreId { get; set; }
        [JsonProperty(Order = 2)]
        public string InvoiceId { get; set; }
    }
}
