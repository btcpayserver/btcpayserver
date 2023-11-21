using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Http;
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
            CheckoutType = CheckoutType.V2;
        }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public NetworkFeeMode NetworkFeeMode { get; set; }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        [DefaultValue(CheckoutType.V2)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public CheckoutType CheckoutType { get; set; }
        public bool RequiresRefundEmail { get; set; }
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
                _DefaultCurrency = value;
                if (!string.IsNullOrEmpty(_DefaultCurrency))
                    _DefaultCurrency = _DefaultCurrency.Trim().ToUpperInvariant();
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

        public string PreferredExchange { get; set; }

        public List<PaymentMethodCriteria> PaymentMethodCriteria { get; set; }
        public string CustomCSS { get; set; }
        public string CustomLogo { get; set; }
        public string HtmlTitle { get; set; }

        public bool AutoDetectLanguage { get; set; }

        public bool RateScripting { get; set; }
#nullable enable
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public InvoiceDataBase.ReceiptOptions ReceiptOptions { get; set; }
#nullable restore
        public string RateScript { get; set; }

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

        public BTCPayServer.Rating.RateRules GetRateRules(BTCPayNetworkProvider networkProvider)
        {
            return GetRateRules(networkProvider, out _);
        }
        public BTCPayServer.Rating.RateRules GetRateRules(BTCPayNetworkProvider networkProvider, out bool preferredSource)
        {
            if (!RateScripting ||
                string.IsNullOrEmpty(RateScript) ||
                !BTCPayServer.Rating.RateRules.TryParse(RateScript, out var rules))
            {
                preferredSource = true;
                return GetDefaultRateRules(networkProvider);
            }
            else
            {
                preferredSource = false;
                rules.Spread = Spread;
                return rules;
            }
        }

        public RateRules GetDefaultRateRules(BTCPayNetworkProvider networkProvider)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var network in networkProvider.GetAll())
            {
                if (network.DefaultRateRules.Length != 0)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"// Default rate rules for {network.CryptoCode}");
                    foreach (var line in network.DefaultRateRules)
                    {
                        builder.AppendLine(line);
                    }
                    builder.AppendLine($"////////");
                    builder.AppendLine();
                }
            }

            var preferredExchange = string.IsNullOrEmpty(PreferredExchange) ? GetRecommendedExchange() : PreferredExchange;
            builder.AppendLine(CultureInfo.InvariantCulture, $"X_X = {preferredExchange}(X_X);");

            BTCPayServer.Rating.RateRules.TryParse(builder.ToString(), out var rules);
            rules.Spread = Spread;
            return rules;
        }

        public static JObject RecommendedExchanges = new()
        {
            { "EUR", "kraken" },
            { "USD", "kraken" },
            { "GBP", "kraken" },
            { "CHF", "kraken" },
            { "GTQ", "bitpay" },
            { "COP", "yadio" },
            { "ARS", "yadio" },
            { "JPY", "bitbank" },
            { "TRY", "btcturk" },
            { "UGX", "yadio"},
            { "RSD", "bitpay"}
        };

        public string GetRecommendedExchange() =>
            RecommendedExchanges.Property(DefaultCurrency)?.Value.ToString() ?? "coingecko";

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

        public List<UIStoresController.StoreEmailRule> EmailRules { get; set; }
        public string BrandColor { get; set; }
        public string LogoFileId { get; set; }
        public string CssFileId { get; set; }

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

        public string SoundFileId { get; set; }

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

        // Replace absolute URL with relative to avoid this issue: https://github.com/btcpayserver/btcpayserver/discussions/4195
        public void NormalizeToRelativeLinks(HttpRequest request)
        {
            var schemeAndHost = $"{request.Scheme}://{request.Host.ToString()}/";
            this.CustomLogo = EnsureRelativeLinks(this.CustomLogo, schemeAndHost);
            this.CustomCSS = EnsureRelativeLinks(this.CustomCSS, schemeAndHost);
        }

        /// <summary>
        /// Make a link relative if possible
        /// </summary>
        /// <param name="value">Example: https://mystore.com/toto.png</param>
        /// <param name="schemeAndHost">Example: https://mystore.com/</param>
        /// <returns>/toto.png</returns>
        private string EnsureRelativeLinks(string value, string schemeAndHost)
        {
            if (value is null)
                return null;
            value = value.Trim();
            if (value.StartsWith(schemeAndHost, StringComparison.OrdinalIgnoreCase))
                return value.Substring(schemeAndHost.Length - 1);
            return value;
        }
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
