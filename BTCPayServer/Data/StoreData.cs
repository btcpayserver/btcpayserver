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

        public IEnumerable<DerivationStrategy> GetDerivationStrategies(BTCPayNetworkProvider networks)
        {
#pragma warning disable CS0618
            bool btcReturned = false;
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
                    var network = networks.GetNetwork(strat.Name);
                    if (network != null)
                    {
                        if (network == networks.BTC && btcReturned)
                            continue;
                        if (strat.Value.Type == JTokenType.Null)
                            continue;
                        yield return BTCPayServer.DerivationStrategy.Parse(strat.Value.Value<string>(), network);
                    }
                }
            }
#pragma warning restore CS0618
        }

        public void SetDerivationStrategy(BTCPayNetwork network, string derivationScheme)
        {
#pragma warning disable CS0618
            JObject strategies = string.IsNullOrEmpty(DerivationStrategies) ? new JObject() : JObject.Parse(DerivationStrategies);
            bool existing = false;
            foreach (var strat in strategies.Properties().ToList())
            {
                if (strat.Name == network.CryptoCode)
                {
                    if (network.IsBTC)
                        DerivationStrategy = null;
                    if (string.IsNullOrEmpty(derivationScheme))
                    {
                        strat.Remove();
                    }
                    else
                    {
                        strat.Value = new JValue(derivationScheme);
                    }
                    existing = true;
                    break;
                }
            }

            if (!existing && string.IsNullOrEmpty(derivationScheme))
            {
                if(network.IsBTC)
                    DerivationStrategy = null;
            }
            else if(!existing)
                strategies.Add(new JProperty(network.CryptoCode, new JValue(derivationScheme)));
            // This is deprecated so we don't have to set anymore
            //if (network.IsBTC)
            //    DerivationStrategy = derivationScheme;
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

    public class StoreBlob
    {
        public StoreBlob()
        {
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
    }
}
