using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletOverviewData
    {
        
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Balance { get; set; }
    }
}
