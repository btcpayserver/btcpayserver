using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public enum InvoiceType
    {
        Standard,
        TopUp
    }
    public class InvoiceDataBase
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceType Type { get; set; }
        public string Currency { get; set; }
        public JObject Metadata { get; set; }
        public CheckoutOptions Checkout { get; set; } = new CheckoutOptions();
        public class CheckoutOptions
        {

            [JsonConverter(typeof(StringEnumConverter))]
            public SpeedPolicy? SpeedPolicy { get; set; }

            public string[] PaymentMethods { get; set; }
            public string DefaultPaymentMethod { get; set; }

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
            public bool? RequiresRefundEmail { get; set; } = null;
            public string DefaultLanguage { get; set; }
        }
    }
    public class InvoiceData : InvoiceDataBase
    {
        public string Id { get; set; }
        public string StoreId { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        public string CheckoutLink { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceExceptionStatus AdditionalStatus { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset MonitoringExpiration { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset ExpirationTime { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset CreatedTime { get; set; }
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public InvoiceStatus[] AvailableStatusesForManualMarking { get; set; }
    }
    public enum InvoiceStatus
    {
        New,
        Processing,
        Expired,
        Invalid,
        Settled
    }
}
