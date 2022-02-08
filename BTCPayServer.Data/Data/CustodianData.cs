namespace BTCPayServer.Data;

public class CustodianData
{
    public string code { get; set; }
    public string name { get; set; }
    public string[] tradableAssetPairs { get; set; }
    public string[] withdrawableAssets { get; set; }
    public string[] depositableAssets { get; set; }
    
}
