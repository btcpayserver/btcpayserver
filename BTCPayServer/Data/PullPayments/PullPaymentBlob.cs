using System;
using System.ComponentModel;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class PullPaymentBlob
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Currency { get; set; }
        public int Divisibility { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Limit { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal MinimumClaim { get; set; }
        public PullPaymentView View { get; set; } = new PullPaymentView();
        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        public TimeSpan? Period { get; set; }

        [DefaultValue(typeof(TimeSpan), "30.00:00:00")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [JsonConverter(typeof(TimeSpanJsonConverter.Days))]
        public TimeSpan BOLT11Expiration { get; set; }


        [JsonProperty(ItemConverterType = typeof(PaymentMethodIdJsonConverter))]
        public PaymentMethodId[] SupportedPaymentMethods { get; set; }

        public bool AutoApproveClaims { get; set; }

        public class PullPaymentView
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string EmbeddedCSS { get; set; }
            public string Email { get; set; }
            public string CustomCSSLink { get; set; }
        }
    }
}
