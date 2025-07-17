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

        public string BrandColor { get; set; }
        public bool? ApplyBrandColorToBackend { get; set; }
        public string LogoUrl { get; set; }
        public string CssUrl { get; set; }
        public string PaymentSoundUrl { get; set; }

        public string SupportUrl { get; set; }

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? InvoiceExpiration { get; set; }

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? DisplayExpirationTimer { get; set; }

        [JsonConverter(typeof(TimeSpanJsonConverter.Days))]
        [JsonProperty("refundBOLT11Expiration", NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? RefundBOLT11Expiration { get; set; }

        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TimeSpan? MonitoringExpiration { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SpeedPolicy? SpeedPolicy { get; set; }
        public string LightningDescriptionTemplate { get; set; }
        public double? PaymentTolerance { get; set; }
        public bool? AnyoneCanCreateInvoice { get; set; }
        public string DefaultCurrency { get; set; }
        public List<string> AdditionalTrackedRates { get; set; }

        public bool? LightningAmountInSatoshi { get; set; }
        public bool? LightningPrivateRouteHints { get; set; }
        public bool? OnChainWithLnInvoiceFallback { get; set; }
        public bool? LazyPaymentMethods { get; set; }
        public bool? RedirectAutomatically { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? Archived { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? ShowRecommendedFee { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? RecommendedFeeBlockTarget { get; set; }

        public string DefaultPaymentMethod { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DefaultLang { get; set; }

        public string HtmlTitle { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public NetworkFeeMode? NetworkFeeMode { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<PaymentMethodCriteriaData> PaymentMethodCriteria { get; set; }

        public bool? PayJoinEnabled { get; set; }

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
