using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class GenericPaymentMethodData
    {
        public bool Enabled { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public JToken Config { get; set; }
        public string PaymentMethodId { get; set; }
    }
    public class UpdatePaymentMethodRequest
    {
        public UpdatePaymentMethodRequest()
        {
            
        }
        public bool? Enabled { get; set; }
        public JToken Config { get; set; }
    }
}
