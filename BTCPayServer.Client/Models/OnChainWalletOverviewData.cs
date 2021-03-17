using BTCPayServer.JsonConverters;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletOverviewData
    {
        
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Balance { get; set; }

        [JsonConverter(typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
