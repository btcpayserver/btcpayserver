﻿using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBXplorer;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BTCPayServer.Services.Rates;
using BTCPayServer.Payments;
using BTCPayServer.JsonConverters;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services;
using System.Security.Claims;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.CoinSwitch;
using BTCPayServer.Security;
using BTCPayServer.Rating;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Data
{
    public class StoreData
    {
        public string Id
        {
            get;
            set;
        }

        public List<UserStore> UserStores
        {
            get; set;
        }
        public List<AppData> Apps
        {
            get; set;
        }
        
        public List<PaymentRequestData> PaymentRequests
        {
            get; set;
        }

        public List<InvoiceData> Invoices { get; set; }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategy
        {
            get; set;
        }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategies
        {
            get;
            set;
        }
        public IEnumerable<ISupportedPaymentMethod> GetSupportedPaymentMethods(BTCPayNetworkProvider networks)
        {
            networks = networks.UnfilteredNetworks;
#pragma warning disable CS0618
            bool btcReturned = false;

            // Legacy stuff which should go away
            if (!string.IsNullOrEmpty(DerivationStrategy))
            {
                btcReturned = true;
                yield return DerivationSchemeSettings.Parse(DerivationStrategy, networks.BTC);
            }


            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    var paymentMethodId = PaymentMethodId.Parse(strat.Name);
                    var network = networks.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
                    if (network != null)
                    {
                        if (network == networks.BTC && paymentMethodId.PaymentType == PaymentTypes.BTCLike && btcReturned)
                            continue;
                        if (strat.Value.Type == JTokenType.Null)
                            continue;
                        yield return
                            paymentMethodId.PaymentType.DeserializeSupportedPaymentMethod(network, strat.Value);
                    }
                }
            }
#pragma warning restore CS0618
        }

        public void SetSupportedPaymentMethod(ISupportedPaymentMethod supportedPaymentMethod)
        {
            SetSupportedPaymentMethod(null, supportedPaymentMethod);
        }

        /// <summary>
        /// Set or remove a new supported payment method for the store
        /// </summary>
        /// <param name="paymentMethodId">The paymentMethodId</param>
        /// <param name="supportedPaymentMethod">The payment method, or null to remove</param>
        public void SetSupportedPaymentMethod(PaymentMethodId paymentMethodId, ISupportedPaymentMethod supportedPaymentMethod)
        {
            if (supportedPaymentMethod != null && paymentMethodId != null && paymentMethodId != supportedPaymentMethod.PaymentId)
            {
                throw new InvalidOperationException("Incoherent arguments, this should never happen");
            }
            if (supportedPaymentMethod == null && paymentMethodId == null)
                throw new ArgumentException($"{nameof(supportedPaymentMethod)} or {nameof(paymentMethodId)} should be specified");
            if (supportedPaymentMethod != null && paymentMethodId == null)
            {
                paymentMethodId = supportedPaymentMethod.PaymentId;
            }

#pragma warning disable CS0618
            JObject strategies = string.IsNullOrEmpty(DerivationStrategies) ? new JObject() : JObject.Parse(DerivationStrategies);
            bool existing = false;
            foreach (var strat in strategies.Properties().ToList())
            {
                var stratId = PaymentMethodId.Parse(strat.Name);
                if (stratId.IsBTCOnChain)
                {
                    // Legacy stuff which should go away
                    DerivationStrategy = null;
                }
                if (stratId == paymentMethodId)
                {
                    if (supportedPaymentMethod == null)
                    {
                        strat.Remove();
                    }
                    else
                    {
                        strat.Value = PaymentMethodExtensions.Serialize(supportedPaymentMethod);
                    }
                    existing = true;
                    break;
                }
            }

            if (!existing && supportedPaymentMethod == null && paymentMethodId.IsBTCOnChain)
            {
                DerivationStrategy = null;
            }
            else if (!existing && supportedPaymentMethod != null)
                strategies.Add(new JProperty(supportedPaymentMethod.PaymentId.ToString(), PaymentMethodExtensions.Serialize(supportedPaymentMethod)));
            DerivationStrategies = strategies.ToString();
#pragma warning restore CS0618
        }

        public string StoreName
        {
            get; set;
        }

        public SpeedPolicy SpeedPolicy
        {
            get; set;
        }

        public string StoreWebsite
        {
            get; set;
        }

        public byte[] StoreCertificate
        {
            get; set;
        }

        [NotMapped]
        [Obsolete]
        public string Role
        {
            get; set;
        }

        public Claim[] GetClaims()
        {
            List<Claim> claims = new List<Claim>();
            claims.AddRange(AdditionalClaims);
#pragma warning disable CS0612 // Type or member is obsolete
            var role = Role;
#pragma warning restore CS0612 // Type or member is obsolete
            if (role == StoreRoles.Owner)
            {
                claims.Add(new Claim(Policies.CanModifyStoreSettings.Key, Id));
            }

            if(role == StoreRoles.Owner || role == StoreRoles.Guest || GetStoreBlob().AnyoneCanInvoice)
            {
                claims.Add(new Claim(Policies.CanCreateInvoice.Key, Id));
            }
            return claims.ToArray();
        }

        public bool HasClaim(string claim)
        {
            return GetClaims().Any(c => c.Type == claim && c.Value == Id);
        }

        public byte[] StoreBlob
        {
            get;
            set;
        }
        [Obsolete("Use GetDefaultPaymentId instead")]
        public string DefaultCrypto { get; set; }
        public List<PairedSINData> PairedSINs { get; set; }
        public IEnumerable<APIKeyData> APIKeys { get; set; }

        [NotMapped]
        public List<Claim> AdditionalClaims { get; set; } = new List<Claim>();

#pragma warning disable CS0618
        public PaymentMethodId GetDefaultPaymentId(BTCPayNetworkProvider networks)
        {
            PaymentMethodId[] paymentMethodIds = GetEnabledPaymentIds(networks);

            var defaultPaymentId = string.IsNullOrEmpty(DefaultCrypto) ? null : PaymentMethodId.Parse(DefaultCrypto);
            var chosen = paymentMethodIds.FirstOrDefault(f => f == defaultPaymentId) ??
                         paymentMethodIds.FirstOrDefault(f => f.CryptoCode == defaultPaymentId?.CryptoCode) ??
                         paymentMethodIds.FirstOrDefault();
            return chosen;
        }

        public PaymentMethodId[] GetEnabledPaymentIds(BTCPayNetworkProvider networks)
        {
            var excludeFilter = GetStoreBlob().GetExcludedPaymentMethods();
            var paymentMethodIds = GetSupportedPaymentMethods(networks).Select(p => p.PaymentId)
                                .Where(a => !excludeFilter.Match(a))
                                .OrderByDescending(a => a.CryptoCode == "BTC")
                                .ThenBy(a => a.CryptoCode)
                                .ThenBy(a => a.PaymentType == PaymentTypes.LightningLike ? 1 : 0)
                                .ToArray();
            return paymentMethodIds;
        }

        public void SetDefaultPaymentId(PaymentMethodId defaultPaymentId)
        {
            DefaultCrypto = defaultPaymentId.ToString();
        }
#pragma warning restore CS0618

        static Network Dummy = Network.Main;

        public StoreBlob GetStoreBlob()
        {
            var result = StoreBlob == null ? new StoreBlob() : new Serializer(Dummy).ToObject<StoreBlob>(Encoding.UTF8.GetString(StoreBlob));
            if (result.PreferredExchange == null)
                result.PreferredExchange = CoinAverageRateProvider.CoinAverageName;
            return result;
        }

        public bool SetStoreBlob(StoreBlob storeBlob)
        {
            var original = new Serializer(Dummy).ToString(GetStoreBlob());
            var newBlob = new Serializer(Dummy).ToString(storeBlob);
            if (original == newBlob)
                return false;
            StoreBlob = Encoding.UTF8.GetBytes(newBlob);
            return true;
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

    public enum NetworkFeeMode
    {
        MultiplePaymentsOnly,
        Always,
        Never
    }
    public class StoreBlob
    {
        public StoreBlob()
        {
            InvoiceExpiration = 15;
            MonitoringExpiration = 1440;
            PaymentTolerance = 0;
            RequiresRefundEmail = true;
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

        [JsonConverter(typeof(UriJsonConverter))]
        public Uri CustomLogo { get; set; }
        [JsonConverter(typeof(UriJsonConverter))]
        public Uri CustomCSS { get; set; }
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

            var preferredExchange = string.IsNullOrEmpty(PreferredExchange) ? "coinaverage" : PreferredExchange;
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
}
