using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class UpdateCreditRequest
{
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Credit { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Charge { get; set; }
    public string Description { get; set; }
    public bool AllowOverdraft { get; set; }
}
