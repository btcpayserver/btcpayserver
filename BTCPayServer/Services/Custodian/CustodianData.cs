using BTCPayServer.Services.Custodian.Client;

namespace BTCPayServer.Services.Custodian;

public class CustodianData
{
    public string code { get; }
    public string name { get;}
    public string[] tradableAssetPairs { get; }

    public CustodianData(ICustodian custodian)
    {
        code = custodian.getCode();
        name = custodian.getName();
        tradableAssetPairs = custodian.getTradableAssetPairs().Result;
    }
}
