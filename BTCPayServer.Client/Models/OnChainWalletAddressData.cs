using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletAddressData
    {
        public string Address { get; set; }
        [JsonConverter(typeof(KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
    }
}
