using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
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

        [JsonProperty(ItemConverterType = typeof(LightMoneyJsonConverter))]
        public LightMoney Capacity { get; set; }

        [JsonProperty(ItemConverterType = typeof(LightMoneyJsonConverter))]
        public LightMoney LocalBalance { get; set; }

        public string ChannelPoint { get; set; }
    }
}
