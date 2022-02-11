using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Data;

public class LedgerEntryData
{
    public string asset { get; set; }
    public decimal qty { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public LedgerEntryType type { get; set; }

    public LedgerEntryData(string asset, decimal qty, LedgerEntryType type)
    {
        this.asset = asset;
        this.qty = qty;
        this.type = type;
    }

    public enum LedgerEntryType
    {
        Trade = 0,
        Fee = 1,
        Withdrawal = 2
    }
}
