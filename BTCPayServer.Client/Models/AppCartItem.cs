using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class AppCartItem
{
    public string Id { get; set; }
    public string Title { get; set; }
    public int Count { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Price { get; set; }
}
