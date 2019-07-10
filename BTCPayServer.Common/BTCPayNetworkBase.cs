using System;
using Newtonsoft.Json;

namespace BTCPayServer
{
    public abstract class BTCPayNetworkBase
    {
        
        public string CryptoCode { get; internal set; }
        public string BlockExplorerLink { get; internal set; }
        public string UriScheme { get; internal set; }
        public string DisplayName { get; set; }

        [Obsolete("Should not be needed")]
        public bool IsBTC
        {
            get
            {
                return CryptoCode == "BTC";
            }
        }

        public string CryptoImagePath { get; set; }

        public int MaxTrackedConfirmation { get; internal set; } = 6;
        public string[] DefaultRateRules { get; internal set; } = Array.Empty<string>();
        public override string ToString()
        {
            return CryptoCode;
        }

        public virtual T ToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public virtual string ToString<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
    }
}