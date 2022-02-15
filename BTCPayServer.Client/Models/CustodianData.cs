namespace BTCPayServer.Client.Models;

public class CustodianData
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string[] TradableAssetPairs { get; set; }
    public string[] WithdrawablePaymentMethods { get; set; }
    public string[] DepositablePaymentMethods { get; set; }
    
}
