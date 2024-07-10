using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class AppItemStats
{
    public string ItemCode { get; set; }
    public string Title { get; set; }
    public int SalesCount { get; set; }
    
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Total { get; set; }
    public string TotalFormatted { get; set; }
}
