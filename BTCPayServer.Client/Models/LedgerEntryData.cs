using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public class LedgerEntryData
{
    public string Asset { get; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Qty { get; }

    [JsonConverter(typeof(StringEnumConverter))]
    public LedgerEntryType Type { get; }

    public LedgerEntryData(string asset, decimal qty, LedgerEntryType type)
    {
        Asset = asset;
        Qty = qty;
        Type = type;
    }

    public enum LedgerEntryType
    {
        Trade = 0,
        Fee = 1,
        Withdrawal = 2
    }
}
