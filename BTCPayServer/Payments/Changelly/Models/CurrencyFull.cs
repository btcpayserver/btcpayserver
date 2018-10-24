using Newtonsoft.Json;

namespace BTCPayServer.Payments.Changelly.Models
{
    public class CurrencyFull
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("fullName")]
        public string FullName { get; set; }
        [JsonProperty("enabled")]
        public bool Enable { get; set; }
        [JsonProperty("payinConfirmations")]
        public int PayInConfirmations { get; set; }
        [JsonProperty("image")]
        public string ImageLink { get; set; }
    }
}