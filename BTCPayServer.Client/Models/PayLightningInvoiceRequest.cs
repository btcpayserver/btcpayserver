using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PayLightningInvoiceRequest
    {
        [JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }
        
        [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
        public float? MaxFeePercent { get; set; }
    }
}
