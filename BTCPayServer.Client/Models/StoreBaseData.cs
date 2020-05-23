using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public abstract class StoreBaseData
    {
        /// <summary>
        /// the name of the store
        /// </summary>
        public string Name { get; set; }

        public string Website { get; set; }
        public int InvoiceExpiration { get; set; } = 15;
        public int MonitoringExpiration { get; set; } = 60;
        
        [JsonConverter(typeof(StringEnumConverter))]
        public SpeedPolicy SpeedPolicy { get; set; }
        public string LightningDescriptionTemplate { get; set; }
        public double PaymentTolerance { get; set; } = 0;
        public bool AnyoneCanCreateInvoice { get; set; }

        public bool ShowRecommendedFee { get; set; }

        public int RecommendedFeeBlockTarget { get; set; }

        public string DefaultLang { get; set; }
        public bool LightningAmountInSatoshi { get; set; }

        public string CustomLogo { get; set; }

        public string CustomCSS { get; set; }

        public string HtmlTitle { get; set; }

        public bool AnyoneCanInvoice { get; set; }

        public bool RedirectAutomatically { get; set; }

        public bool RequiresRefundEmail { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public NetworkFeeMode NetworkFeeMode { get; set; }

        public bool PayJoinEnabled { get; set; }

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
