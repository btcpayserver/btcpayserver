using System;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreateLightningInvoiceRequest
    {
        [JsonProperty(ItemConverterType = typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }
        
    }
}
