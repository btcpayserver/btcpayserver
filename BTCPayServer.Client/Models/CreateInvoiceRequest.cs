using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public JObject Metadata { get; set; }
        public CheckoutOptions Checkout { get; set; } = new CheckoutOptions();

        public class CheckoutOptions
        {

            [JsonConverter(typeof(StringEnumConverter))]
            public SpeedPolicy? SpeedPolicy { get; set; }

            public string[] PaymentMethods { get; set; }

            [JsonConverter(typeof(TimeSpanJsonConverter.Minutes))]
            [JsonProperty("expirationMinutes")]
            public TimeSpan? Expiration { get; set; }
            [JsonConverter(typeof(TimeSpanJsonConverter.Minutes))]
            [JsonProperty("monitoringMinutes")]
            public TimeSpan? Monitoring { get; set; }

            public double? PaymentTolerance { get; set; }
            [JsonProperty("redirectURL")]
            public string RedirectURL { get; set; }

            public bool? RedirectAutomatically { get; set; }
            public string DefaultLanguage { get; set; }
        }
    }
}
