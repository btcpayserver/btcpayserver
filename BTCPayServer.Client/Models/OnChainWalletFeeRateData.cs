using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletFeeRateData
    {
        [JsonConverter(typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
