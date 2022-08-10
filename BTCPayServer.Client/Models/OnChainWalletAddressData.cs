using System.Collections.Generic;
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

        public string PaymentLink { get; set; }
        public List<LabelData> Labels { get; set; }
    }
}
