using System.Collections.Generic;
using BTCPayServer.Lightning;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class LightningNodeInformationData
    {
        public IEnumerable<string> NodeInfoList { get; set; }
        public int BlockHeight { get; set; }
    }

    public class LightningChannelData
    {
        public string RemoteNode { get; set; }

        public bool IsPublic { get; set; }

        public bool IsActive { get; set; }

        [JsonProperty(ItemConverterType = typeof(MoneyJsonConverter))]
        public LightMoney Capacity { get; set; }

        [JsonProperty(ItemConverterType = typeof(MoneyJsonConverter))]
        public LightMoney LocalBalance { get; set; }

        public string ChannelPoint { get; set; }
    }
}
