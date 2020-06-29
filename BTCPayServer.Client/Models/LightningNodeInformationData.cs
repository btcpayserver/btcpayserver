using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class LightningNodeInformationData
    {
        [JsonProperty("nodeURIs", ItemConverterType = typeof(NodeUriJsonConverter))]
        public NodeInfo[] NodeURIs { get; set; }
        public int BlockHeight { get; set; }
    }

    public class LightningChannelData
    {
        public string RemoteNode { get; set; }

        public bool IsPublic { get; set; }

        public bool IsActive { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Capacity { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney LocalBalance { get; set; }

        public string ChannelPoint { get; set; }
    }
}
