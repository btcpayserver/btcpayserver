using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreatePayoutRequest
    {
        public string Destination { get; set; }
        [JsonConverter(typeof(DecimalStringJsonConverter))]
        public decimal? Amount { get; set; }
        public string PaymentMethod { get; set; }
    }
}
