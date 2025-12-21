using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Client.Models;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Rating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class StoreBlob
    {
        public static string StandardDefaultCurrency = "USD";

        public StoreBlob()
        {
            InvoiceExpiration = TimeSpan.FromMinutes(15);
            DisplayExpirationTimer = TimeSpan.FromMinutes(5);
            RefundBOLT11Expiration = TimeSpan.FromDays(30);
            MonitoringExpiration = TimeSpan.FromDays(1);
            PaymentTolerance = 0;
            ShowRecommendedFee = true;
            RecommendedFeeBlockTarget = 1;
            PaymentMethodCriteria = new List<PaymentMethodCriteria>();
            ReceiptOptions = InvoiceDataBase.ReceiptOptions.CreateDefault();
        }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public NetworkFeeMode NetworkFeeMode { get; set; }

        public bool LightningAmountInSatoshi { get; set; }
        public bool LightningPrivateRouteHints { get; set; }
        public bool OnChainWithLnInvoiceFallback { get; set; }
        public bool LazyPaymentMethods { get; set; }
        public bool RedirectAutomatically { get; set; }
        public bool ShowRecommendedFee { get; set; }
        public int RecommendedFeeBlockTarget { get; set; }
        string _DefaultCurrency;
        public string DefaultCurrency
        {
            get
            {
                return string.IsNullOrEmpty(_DefaultCurrency) ? StandardDefaultCurrency : _DefaultCurrency;
            }
            set
            {
                _DefaultCurrency = NormalizeCurrency(value);
            }
        }

        public string StoreSupportUrl { get; set; }

        CurrencyPair[] _DefaultCurrencyPairs;
        [JsonProperty("defaultCurrencyPairs", ItemConverterType = typeof(CurrencyPairJsonConverter))]
        public CurrencyPair[] DefaultCurrencyPairs
        {
            get
            {
                return _DefaultCurrencyPairs ?? Array.Empty<CurrencyPair>();
            }
            set
            {
                _DefaultCurrencyPairs = value;
            }
        }

        public string GetDefaultCurrencyPairString()
        {
            return string.Join(',', DefaultCurrencyPairs.Select(c => c.ToString()));
        }

        public string DefaultLang { get; set; }
        [DefaultValue(typeof(TimeSpan), "1.00:00:00")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [JsonConverter(typeof(TimeSpanJsonConverter.Minutes))]
        public TimeSpan MonitoringExpiration
        {
            get;
            set;
        }

        [DefaultValue(typeof(TimeSpan), "00:15:00")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [JsonConverter(typeof(TimeSpanJsonConverter.Minutes))]
        public TimeSpan InvoiceExpiration { get; set; }

        [DefaultValue(typeof(TimeSpan), "00:05:00")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [JsonConverter(typeof(TimeSpanJsonConverter.Minutes))]
        public TimeSpan DisplayExpirationTimer { get; set; }

        public decimal Spread { get; set; } = 0.0m;
        public class RateSettings
        {
            /// <summary>
            /// This may be null. Use <see cref="GetPreferredExchange(DefaultRulesCollection)"/> instead if you want to return a valid exchange
            /// </summary>
            public string PreferredExchange { get; set; }
            public bool RateScripting { get; set; }
            public string RateScript { get; set; }

            /// <summary>
            /// Use the preferred exchange of the store, or the recommended exchange from the default currency
            /// </summary>
            /// <param name="defaultRules"></param>
            /// <returns></returns>
            public string GetPreferredExchange(DefaultRulesCollection defaultRules, string defaultCurrency)
            {
                return string.IsNullOrEmpty(PreferredExchange) ? defaultRules.GetRecommendedExchange(defaultCurrency) : PreferredExchange;
            }
            public BTCPayServer.Rating.RateRules GetRateRules(DefaultRulesCollection defaultRules, decimal spread)
            {
                return GetRateRules(defaultRules, spread, out _);
            }
            public BTCPayServer.Rating.RateRules GetRateRules(DefaultRulesCollection defaultRules, decimal spread, out bool preferredSource)
            {
                if (!RateScripting ||
                    string.IsNullOrEmpty(RateScript) ||
                    !BTCPayServer.Rating.RateRules.TryParse(RateScript, out var rules))
                {
                    preferredSource = true;
                    return GetDefaultRateRules(defaultRules, spread);
                }
                else
                {
                    preferredSource = false;
                    rules.Spread = spread;
                    return rules;
                }
            }

            public RateRules GetDefaultRateRules(DefaultRulesCollection defaultRules, decimal spread)
            {
                var rules = defaultRules.WithPreferredExchange(PreferredExchange);
                rules.Spread = spread;
                return rules;
            }
        }

        #nullable  enable
        public void SetRateSettings(RateSettings? rateSettings, bool fallback)
        {
            if (fallback)
                FallbackRateSettings = rateSettings;
            else
                PrimaryRateSettings = rateSettings;
        }

        public RateSettings GetOrCreateRateSettings(bool fallback)
        {
            var settings = GetRateSettings(fallback);
            if (settings is null)
            {
                settings = new RateSettings();
                SetRateSettings(settings, fallback);
            }
            return settings;
        }
        public RateSettings? GetRateSettings(bool fallback)
        {
            if (fallback)
                return FallbackRateSettings;
            PrimaryRateSettings ??= new();
            return PrimaryRateSettings;
        }
        public RateSettings? PrimaryRateSettings { get; set; }
        public RateSettings? FallbackRateSettings { get; set; }
#nullable  restore


        public List<PaymentMethodCriteria> PaymentMethodCriteria { get; set; }
        public string HtmlTitle { get; set; }

        public bool AutoDetectLanguage { get; set; }

#nullable enable
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public InvoiceDataBase.ReceiptOptions ReceiptOptions { get; set; }
#nullable restore

        public bool AnyoneCanInvoice { get; set; }

        string _LightningDescriptionTemplate;
        public string LightningDescriptionTemplate
        {
            get
            {
                return _LightningDescriptionTemplate ?? "Paid to {StoreName} (Order ID: {OrderId})";
            }
            set
            {
                _LightningDescriptionTemplate = value;
            }
        }

        [DefaultValue(0)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public double PaymentTolerance { get; set; }

        [Obsolete("Use GetExcludedPaymentMethods instead")]
        public string[] ExcludedPaymentMethods { get; set; }

        public EmailSettings EmailSettings { get; set; }
        public bool PayJoinEnabled { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();

        [DefaultValue(typeof(TimeSpan), "30.00:00:00")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [JsonConverter(typeof(TimeSpanJsonConverter.Days))]
        public TimeSpan RefundBOLT11Expiration { get; set; }

        public string BrandColor { get; set; }
        public bool ApplyBrandColorToBackend { get; set; }

        [JsonConverter(typeof(UnresolvedUriJsonConverter))]
        public UnresolvedUri LogoUrl { get; set; }
        [JsonConverter(typeof(UnresolvedUriJsonConverter))]
        public UnresolvedUri CssUrl { get; set; }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool ShowPayInWalletButton { get; set; } = true;

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool ShowStoreHeader { get; set; } = true;

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool CelebratePayment { get; set; } = true;

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool PlaySoundOnPayment { get; set; }

		[JsonConverter(typeof(UnresolvedUriJsonConverter))]
		public UnresolvedUri PaymentSoundUrl { get; set; }

        public IPaymentFilter GetExcludedPaymentMethods()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (ExcludedPaymentMethods == null || ExcludedPaymentMethods.Length == 0)
                return PaymentFilter.Never();

            return PaymentFilter.Any(ExcludedPaymentMethods
                                    .Select(PaymentMethodId.TryParse).Where(id => id != null)
                                    .Select(PaymentFilter.WhereIs).ToArray());
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public bool IsExcluded(PaymentMethodId paymentMethodId)
        {
            return GetExcludedPaymentMethods().Match(paymentMethodId);
        }

        public void SetExcluded(PaymentMethodId paymentMethodId, bool value)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var methods = new HashSet<string>(ExcludedPaymentMethods ?? Array.Empty<string>());
            if (value)
                methods.Add(paymentMethodId.ToString());
            else
                methods.Remove(paymentMethodId.ToString());
            ExcludedPaymentMethods = methods.ToArray();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public RateRulesCollection GetRateRules(DefaultRulesCollection defaultRules)
        {
            return new(
                (PrimaryRateSettings ?? new()).GetRateRules(defaultRules, Spread),
                FallbackRateSettings?.GetRateRules(defaultRules, Spread));
        }

        public HashSet<string> GetTrackedRates() => AdditionalTrackedRates.Concat([DefaultCurrency]).ToHashSet();

        private string[] _additionalTrackedRates;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string[] AdditionalTrackedRates
        {
            get
            {
                return _additionalTrackedRates ?? Array.Empty<string>();
            }
            set
            {
                if (value is not null)
                    _additionalTrackedRates = value
                        .Select(NormalizeCurrency)
                        .Where(v => v is not null).ToArray();
                else
                    _additionalTrackedRates = null;
            }
        }

        private string NormalizeCurrency(string v) =>
            v is null ? null :
            Regex.Replace(v.ToUpperInvariant(), "[^A-Z]", "").Trim() is { Length: > 0 } normalized ? normalized : null;
    }
    public class PaymentMethodCriteria
    {
        [JsonConverter(typeof(PaymentMethodIdJsonConverter))]
        public PaymentMethodId PaymentMethod { get; set; }
        [JsonConverter(typeof(CurrencyValueJsonConverter))]
        public CurrencyValue Value { get; set; }
        public bool Above { get; set; }
    }
}
