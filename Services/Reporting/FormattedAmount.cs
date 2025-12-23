using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Reporting
{
    public class FormattedAmount
    {
        public FormattedAmount(decimal value, int divisibility)
        {
            Value = value;
            Divisibility = divisibility;
        }
        [JsonProperty("v")]
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Value { get; set; }
        [JsonProperty("d")]
        public int Divisibility { get; set; }

        public JObject ToJObject()
        {
            return JObject.FromObject(this);
        }
    }
}
