using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class TradeRequestData
{
    public string FromAsset { set; get; }
    public string ToAsset { set; get; }
    [JsonConverter(typeof(JsonConverters.TradeQuantityJsonConverter))]
    public TradeQuantity Qty { set; get; }
}
