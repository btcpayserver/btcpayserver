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

        public string DerivationStrategy
        {
            get; set;
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

        [Obsolete("Use GetSupportedCryptoCurrencies() instead")]
        public string[] SupportedCryptoCurrencies { get; set; }

        public string[] GetSupportedCryptoCurrencies()
        {
#pragma warning disable CS0618
            if(SupportedCryptoCurrencies == null)
            {
                return new string[] { "BTC" };
            }
            return SupportedCryptoCurrencies;
#pragma warning restore CS0618
        }
    }
}
