using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using MoneyJsonConverter = BTCPayServer.Client.JsonConverters.MoneyJsonConverter;

namespace BTCPayServer.Client.Models
{
    public class OpenLightningChannelRequest
    {
        [JsonProperty(PropertyName = "nodeURI")]
        [JsonConverter(typeof(NodeInfoJsonConverter))]
        public NodeInfo NodeURI { get; set; }
        [JsonProperty(ItemConverterType = typeof(MoneyJsonConverter))]
        public Money ChannelAmount { get; set; }

        [JsonProperty(ItemConverterType = typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
