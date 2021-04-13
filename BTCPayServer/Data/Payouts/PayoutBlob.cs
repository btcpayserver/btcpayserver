using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class PayoutBlob
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? CryptoAmount { get; set; }
        public int MinimumConfirmation { get; set; } = 1;
        public string Destination { get; set; }
        public int Revision { get; set; }
    }
}
