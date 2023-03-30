namespace BTCPayServer.Abstractions.Custodians;

public class AssetBalancesUnavailableException : CustodianApiException
{
    public AssetBalancesUnavailableException(System.Exception e) : base(500, "asset-balances-unavailable", $"Cannot fetch the asset balances: {e.Message}", e)
    {
    }

    public AssetBalancesUnavailableException(string errorMsg) : base(500, "asset-balances-unavailable", $"Cannot fetch the asset balances: {errorMsg}")
    {
    }
}
