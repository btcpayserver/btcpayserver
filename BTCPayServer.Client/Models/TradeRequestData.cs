namespace BTCPayServer.Client.Models;

public class TradeRequestData
{
    public string FromAsset { set; get; }
    public string ToAsset { set; get; }
    public string Qty { set; get; }
}
