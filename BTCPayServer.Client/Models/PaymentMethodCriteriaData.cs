using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class PaymentMethodCriteriaData
{
    public string PaymentMethod { get; set; }
    public string CurrencyCode { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Amount { get; set; }
    public bool Above { get; set; }
}
