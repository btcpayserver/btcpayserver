using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class CreditModel
{
    public string Currency { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Value { get; set; }
}
