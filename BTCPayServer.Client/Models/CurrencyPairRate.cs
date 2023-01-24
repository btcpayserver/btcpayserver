using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CurrencyPairRate
    {

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }
        
        [JsonProperty(PropertyName = "cryptoCode")]
        public string CryptoCode { get; set; }

        [JsonProperty(PropertyName = "currencyPair")]
        public string CurrencyPair { get; set; }

        [JsonProperty(PropertyName = "code")]
        public string Code { get; set; }
        
        [JsonProperty(PropertyName = "rate")]
        public decimal Value { get; set; }
    }
}
