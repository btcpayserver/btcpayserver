using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest
    {
        [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Metadata { get; set; }
        public string CustomerEmail { get; set; }
        public CheckoutOptions Checkout { get; set; } = new CheckoutOptions();

        public class CheckoutOptions
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public SpeedPolicy? SpeedPolicy { get; set; }

            public string[] PaymentMethods { get; set; }
            public bool? RedirectAutomatically { get; set; }
            public string RedirectUri { get; set; }
            public Uri WebHook { get; set; }

            [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
            public DateTimeOffset? ExpirationTime { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public double? PaymentTolerance { get; set; }
        }
    }
}
