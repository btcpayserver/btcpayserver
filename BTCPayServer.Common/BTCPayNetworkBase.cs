using System;

namespace BTCPayServer
{
    public abstract class BTCPayNetworkBase
    {
        public bool ShowSyncSummary { get; set; } = true;
        public string CryptoCode { get; internal set; }
        public string BlockExplorerLink { get; internal set; }
        public string DisplayName { get; set; }
        public int Divisibility { get; set; } = 8;
        [Obsolete("Should not be needed")]
        public bool IsBTC
        {
            get
            {
                return CryptoCode == "BTC";
            }
        }

        public string CryptoImagePath { get; set; }
        public string[] DefaultRateRules { get; internal set; } = Array.Empty<string>();
        public override string ToString()
        {
            return CryptoCode;
        }

        public virtual T ToObject<T>(string json)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(json, null);
        }

        public virtual string ToString<T>(T obj)
        {
            return NBitcoin.JsonConverters.Serializer.ToString(obj, null);
        }
    }
}
