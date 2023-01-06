using System;
using BTCPayServer.Lightning;
using NBitcoin;
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

        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public bool DescriptionHashOnly { get; set; }
        [JsonConverter(typeof(JsonConverters.TimeSpanJsonConverter.Seconds))]
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }

    }
}
