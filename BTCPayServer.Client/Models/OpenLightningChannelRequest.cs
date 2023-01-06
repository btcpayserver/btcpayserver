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
        [JsonConverter(typeof(NodeUriJsonConverter))]
        [JsonProperty("nodeURI")]
        public BTCPayServer.Lightning.NodeInfo NodeURI { get; set; }
        [JsonConverter(typeof(MoneyJsonConverter))]
        public Money ChannelAmount { get; set; }

        [JsonConverter(typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
