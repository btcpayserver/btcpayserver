using BTCPayServer.Models;
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
#pragma warning disable CS0618
            bool btcReturned = false;

            // Legacy stuff which should go away
            if (!string.IsNullOrEmpty(DerivationStrategy))
            {
                if (networks.BTC != null)
                {
                    btcReturned = true;
                    yield return BTCPayServer.DerivationStrategy.Parse(DerivationStrategy, networks.BTC);
                }
            }


            if (!string.IsNullOrEmpty(DerivationStrategies))
            {
                JObject strategies = JObject.Parse(DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    var paymentMethodId = PaymentMethodId.Parse(strat.Name);
                    var network = networks.GetNetwork(paymentMethodId.CryptoCode);
                    if (network != null)
                    {
                        if (network == networks.BTC && paymentMethodId.PaymentType == PaymentTypes.BTCLike && btcReturned)
                            continue;
                        if (strat.Value.Type == JTokenType.Null)
                            continue;
                        yield return PaymentMethodExtensions.Deserialize(paymentMethodId, strat.Value, network);
                    }
                }
            }
#pragma warning restore CS0618
        }

        /// <summary>
        /// Set or remove a new supported payment method for the store
        /// </summary>
        /// <param name="paymentMethodId">The paymentMethodId</param>
        /// <param name="supportedPaymentMethod">The payment method, or null to remove</param>
        public void SetSupportedPaymentMethod(PaymentMethodId paymentMethodId, ISupportedPaymentMethod supportedPaymentMethod)
        {
            if (supportedPaymentMethod != null && paymentMethodId != supportedPaymentMethod.PaymentId)
                throw new InvalidOperationException("Argument mismatch");

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

            if(!existing && supportedPaymentMethod == null && paymentMethodId.IsBTCOnChain)
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
        public string Role
        {
            get; set;
        }
        public byte[] StoreBlob
        {
            get;
            set;
        }
        [Obsolete("Use GetDefaultCrypto instead")]
        public string DefaultCrypto { get; set; }

#pragma warning disable CS0618
        public string GetDefaultCrypto()
        {
            return DefaultCrypto ?? "BTC";
        }
        public void SetDefaultCrypto(string defaultCryptoCurrency)
        {
            DefaultCrypto = defaultCryptoCurrency;
        }
#pragma warning restore CS0618

        static Network Dummy = Network.Main;

        public StoreBlob GetStoreBlob()
        {
            return StoreBlob == null ? new StoreBlob() : new Serializer(Dummy).ToObject<StoreBlob>(Encoding.UTF8.GetString(StoreBlob));
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

    public class RateRule
    {
        public RateRule()
        {
            RuleName = "Multiplier";
        }
        public string RuleName { get; set; }

        public double Multiplier { get; set; }

        public decimal Apply(BTCPayNetwork network, decimal rate)
        {
            return rate * (decimal)Multiplier;
        }
    }

    public class StoreBlob
    {
        public StoreBlob()
        {
            InvoiceExpiration = 15;
            MonitoringExpiration = 60;
        }
        public bool NetworkFeeDisabled
        {
            get; set;
        }
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

        public void SetRateMultiplier(double rate)
        {
            RateRules = new List<RateRule>();
            RateRules.Add(new RateRule() { Multiplier = rate });
        }
        public decimal GetRateMultiplier()
        {
            decimal rate = 1.0m;
            if (RateRules == null || RateRules.Count == 0)
                return rate;
            foreach (var rule in RateRules)
            {
                rate = rule.Apply(null, rate);
            }
            return rate;
        }

        public List<RateRule> RateRules { get; set; } = new List<RateRule>();
        public string PreferredExchange { get; set; }

        public IRateProvider ApplyRateRules(BTCPayNetwork network, IRateProvider rateProvider)
        {
            if (!PreferredExchange.IsCoinAverage())
            {
                // If the original rateProvider is a cache, use the same inner provider as fallback, and same memory cache to wrap it all
                if (rateProvider is CachedRateProvider cachedRateProvider)
                {
                    rateProvider = new FallbackRateProvider(new IRateProvider[] {
                        new CoinAverageRateProvider(network.CryptoCode) { Exchange = PreferredExchange },
                        cachedRateProvider.Inner
                    });
                    rateProvider = new CachedRateProvider(network.CryptoCode, rateProvider, cachedRateProvider.MemoryCache) { AdditionalScope = PreferredExchange };
                }
                else
                {
                    rateProvider = new FallbackRateProvider(new IRateProvider[] {
                        new CoinAverageRateProvider(network.CryptoCode) { Exchange = PreferredExchange },
                        rateProvider
                    });
                }
            }
            if (RateRules == null || RateRules.Count == 0)
                return rateProvider;
            return new TweakRateProvider(network, rateProvider, RateRules.ToList());
        }
    }
}
