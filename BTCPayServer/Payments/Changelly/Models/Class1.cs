using Newtonsoft.Json;

namespace BTCPayServer.Payments.Changelly.Models
{
    public class ExchangeResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRPC { get; set; }
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("result")]
        public object Result { get; set; }
        [JsonProperty("error")]
        public Error Error { get; set; }
    }
    
    public class Response
    {
        [JsonProperty("jsonrpc")]
        public string JsonRPC { get; set; }
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("result")]
        public object Result { get; set; }
        [JsonProperty("error")]
        public Error Error { get; set; }
    }
    public class Error
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } 
    }
    
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
