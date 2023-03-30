using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class TradeQuoteResponseData
{
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Bid { get; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Ask { get; }
    public string ToAsset { get; }
    public string FromAsset { get; }

    public TradeQuoteResponseData(string fromAsset, string toAsset, decimal bid, decimal ask)
    {
        FromAsset = fromAsset;
        ToAsset = toAsset;
        Bid = bid;
        Ask = ask;
    }
}
