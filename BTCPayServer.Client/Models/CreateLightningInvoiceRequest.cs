using System;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreateLightningInvoiceRequest
    {
        public CreateLightningInvoiceRequest()
        {

        }
        public CreateLightningInvoiceRequest(LightMoney amount, string description, TimeSpan expiry)
        {
            Amount = amount;
            Description = description;
            Expiry = expiry;
        }
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        [JsonConverter(typeof(JsonConverters.TimeSpanJsonConverter))]
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }

    }
}
