using BTCPayServer.Services.Custodian.Client;

namespace BTCPayServer.Services.Custodian;

public class CustodianData
{
    public string code { get; set; }
    public string name { get; set; }
    public string[] tradableAssetPairs { get; set; }
}
