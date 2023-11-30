using System;
using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public abstract class StoreBaseData
    {
        /// <summary>
        /// the name of the store
        /// </summary>
        public string Name { get; set; }

        public string Website { get; set; }

        public string SupportUrl { get; set; }

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan InvoiceExpiration { get; set; } = TimeSpan.FromMinutes(15);

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan DisplayExpirationTimer { get; set; } = TimeSpan.FromMinutes(5);

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan MonitoringExpiration { get; set; } = TimeSpan.FromMinutes(60);

        [JsonConverter(typeof(StringEnumConverter))]
        public SpeedPolicy SpeedPolicy { get; set; }
        public string LightningDescriptionTemplate { get; set; }
        public double PaymentTolerance { get; set; } = 0;
        public bool AnyoneCanCreateInvoice { get; set; }
        public string DefaultCurrency { get; set; }
        public bool RequiresRefundEmail { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public CheckoutType? CheckoutType { get; set; }

        public bool LightningAmountInSatoshi { get; set; }
        public bool LightningPrivateRouteHints { get; set; }
        public bool OnChainWithLnInvoiceFallback { get; set; }
        public bool LazyPaymentMethods { get; set; }
        public bool RedirectAutomatically { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool Archived { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool ShowRecommendedFee { get; set; } = true;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int RecommendedFeeBlockTarget { get; set; } = 1;

        public string DefaultPaymentMethod { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DefaultLang { get; set; } = "en";

        public string CustomLogo { get; set; }

        public string CustomCSS { get; set; }

        public string HtmlTitle { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public NetworkFeeMode NetworkFeeMode { get; set; } = NetworkFeeMode.Never;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<PaymentMethodCriteriaData> PaymentMethodCriteria { get; set; }

        public bool PayJoinEnabled { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoDetectLanguage { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? ShowPayInWalletButton { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? ShowStoreHeader { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? CelebratePayment { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? PlaySoundOnPayment { get; set; }

        public InvoiceData.ReceiptOptions Receipt { get; set; }


        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
    }

    public enum CheckoutType
    {
        V1,
        V2
    }

    public enum NetworkFeeMode
    {
        MultiplePaymentsOnly,
        Always,
        Never
    }

    public enum SpeedPolicy
    {
        HighSpeed = 0,
        MediumSpeed = 1,
        LowSpeed = 2,
        LowMediumSpeed = 3
    }
}
