using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OpenChannelRequest
    {
        public ConnectToNodeRequest Node { get; set; }
        [JsonProperty(ItemConverterType = typeof(MoneyJsonConverter))]
        public Money ChannelAmount { get; set; }

        [JsonProperty(ItemConverterType = typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
