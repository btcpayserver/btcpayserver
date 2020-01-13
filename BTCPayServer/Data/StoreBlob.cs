using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.CoinSwitch;
using BTCPayServer.Rating;
using BTCPayServer.Services.Mails;
using Newtonsoft.Json;
using System.Text;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Data
{
    public class StoreBlob
    {
        public StoreBlob()
        {
            InvoiceExpiration = 15;
            MonitoringExpiration = 1440;
            PaymentTolerance = 0;
            ShowRecommendedFee = true;
            RecommendedFeeBlockTarget = 1;
        }

        [Obsolete("Use NetworkFeeMode instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool? NetworkFeeDisabled
        {
            get; set;
        }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public NetworkFeeMode NetworkFeeMode
        {
            get;
            set;
        }

        public bool RequiresRefundEmail { get; set; }

        public bool ShowRecommendedFee { get; set; }

        public int RecommendedFeeBlockTarget { get; set; }

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
        [DefaultValue(60)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MonitoringExpiration
        {
            get;
            set;
        }

        [DefaultValue(15)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int InvoiceExpiration
        {
            get;
            set;
        }

        public decimal Spread { get; set; } = 0.0m;

        [Obsolete]
        public List<RateRule_Obsolete> RateRules { get; set; } = new List<RateRule_Obsolete>();
        public string PreferredExchange { get; set; }

        [JsonConverter(typeof(CurrencyValueJsonConverter))]
        public CurrencyValue OnChainMinValue { get; set; }
        [JsonConverter(typeof(CurrencyValueJsonConverter))]
        public CurrencyValue LightningMaxValue { get; set; }
        public bool LightningAmountInSatoshi { get; set; }

        public string CustomLogo { get; set; }
        
        public string CustomCSS { get; set; }
        public string HtmlTitle { get; set; }

        public bool RateScripting { get; set; }

        public string RateScript { get; set; }

        public bool AnyoneCanInvoice { get; set; }

        public ChangellySettings ChangellySettings { get; set; }
        public CoinSwitchSettings CoinSwitchSettings { get; set; }


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
            if (!RateScripting ||
                string.IsNullOrEmpty(RateScript) ||
                !BTCPayServer.Rating.RateRules.TryParse(RateScript, out var rules))
            {
                return GetDefaultRateRules(networkProvider);
            }
            else
            {
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
                    builder.AppendLine($"// Default rate rules for {network.CryptoCode}");
                    foreach (var line in network.DefaultRateRules)
                    {
                        builder.AppendLine(line);
                    }
                    builder.AppendLine($"////////");
                    builder.AppendLine();
                }
            }

            var preferredExchange = string.IsNullOrEmpty(PreferredExchange) ? CoinGeckoRateProvider.CoinGeckoName : PreferredExchange;
            builder.AppendLine($"X_X = {preferredExchange}(X_X);");

            BTCPayServer.Rating.RateRules.TryParse(builder.ToString(), out var rules);
            rules.Spread = Spread;
            return rules;
        }

        [Obsolete("Use GetExcludedPaymentMethods instead")]
        public string[] ExcludedPaymentMethods { get; set; }

        [Obsolete("Use DerivationSchemeSettings instead")]
        public Dictionary<string, string> WalletKeyPathRoots { get; set; }

        public EmailSettings EmailSettings { get; set; }
        public bool RedirectAutomatically { get; set; }

        public IPaymentFilter GetExcludedPaymentMethods()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (ExcludedPaymentMethods == null || ExcludedPaymentMethods.Length == 0)
                return PaymentFilter.Never();
            return PaymentFilter.Any(ExcludedPaymentMethods.Select(p => PaymentFilter.WhereIs(PaymentMethodId.Parse(p))).ToArray());
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
    }
    public class RateRule_Obsolete
    {
        public RateRule_Obsolete()
        {
            RuleName = "Multiplier";
        }
        public string RuleName { get; set; }

        public double Multiplier { get; set; }

        public decimal Apply(BTCPayNetworkBase network, decimal rate)
        {
            return rate * (decimal)Multiplier;
        }
    }
}
