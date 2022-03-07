namespace BTCPayServer.Abstractions.Custodians.Client.Exception;

public class AssetBalancesUnavailableException : CustodianApiException
{
    public System.Exception OriginalException { get; }
    
    public AssetBalancesUnavailableException(System.Exception e) : base(500, "asset-balances-unavailable", $"Cannot fetch the asset balances: {e.Message}")
    {
        this.OriginalException = e;
    }

    
}
