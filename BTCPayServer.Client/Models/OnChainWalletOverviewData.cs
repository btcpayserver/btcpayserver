using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletOverviewData
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Balance { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal UnconfirmedBalance { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal ConfirmedBalance { get; set; }

        public string Label { get; set; }
    }
}
