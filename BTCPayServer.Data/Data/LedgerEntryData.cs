namespace BTCPayServer.Data;

public class LedgerEntryData
{
    public string asset { get; set; }
    public decimal qty { get; set; }
    public string comment { get; set; }

    public LedgerEntryData(string asset, decimal qty, string comment)
    {
        this.asset = asset;
        this.qty = qty;
        this.comment = comment;
    }
}
