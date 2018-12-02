using Newtonsoft.Json;

namespace BTCPayServer.Payments.Changelly.Models
{
    public class Error
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } 
    }
}