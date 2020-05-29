using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using MoneyJsonConverter = BTCPayServer.Client.JsonConverters.MoneyJsonConverter;

namespace BTCPayServer.Client.Models
{
    public class OpenLightningChannelRequest
    {
        public ConnectToNodeRequest Node { get; set; }
        [JsonProperty(ItemConverterType = typeof(MoneyJsonConverter))]
        public Money ChannelAmount { get; set; }

        [JsonProperty(ItemConverterType = typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
