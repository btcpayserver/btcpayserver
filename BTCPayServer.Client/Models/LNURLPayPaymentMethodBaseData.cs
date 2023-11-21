using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class LNURLPayPaymentMethodBaseData
    {
        public bool UseBech32Scheme { get; set; }
        
        [JsonProperty("lud12Enabled")]
        public bool LUD12Enabled { get; set; }

        public LNURLPayPaymentMethodBaseData()
        {

        }
    }
}
