using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest
    {
        [JsonProperty(ItemConverterType = typeof(DecimalDoubleStringJsonConverter))]
        public decimal Amount
        {
            get;
            set;
        }

        public string Currency
        {
            get;
            set;
        }

        public ProductInformation Metadata { get; set; }

        public BuyerInformation Customer { get; set; } = new BuyerInformation();

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

            [JsonProperty(ItemConverterType = typeof(DecimalDoubleStringJsonConverter))]
            public double? PaymentTolerance { get; set; }
        }

        public class BuyerInformation
        {
            [JsonProperty(PropertyName = "buyerName")]
            public string BuyerName
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerEmail")]
            public string BuyerEmail
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerCountry")]
            public string BuyerCountry
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerZip")]
            public string BuyerZip
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerState")]
            public string BuyerState
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerCity")]
            public string BuyerCity
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerAddress2")]
            public string BuyerAddress2
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerAddress1")]
            public string BuyerAddress1
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "buyerPhone")]
            public string BuyerPhone
            {
                get;
                set;
            }
        }

        public class ProductInformation
        {
            public string OrderId { get; set; }
            public string PosData { get; set; }

            public string ItemDesc
            {
                get;
                set;
            }

            public string ItemCode
            {
                get;
                set;
            }

            public bool Physical
            {
                get;
                set;
            }

            public decimal? TaxIncluded
            {
                get;
                set;
            }
        }
    }
}
