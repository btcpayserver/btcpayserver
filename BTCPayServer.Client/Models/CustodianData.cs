using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class CustodianData
{
    public string Code { get; set; }
    public string Name { get; set; }
    public Dictionary<string, AssetPairData> TradableAssetPairs { get; set; }
    public string[] WithdrawablePaymentMethods { get; set; }
    public string[] DepositablePaymentMethods { get; set; }

}
