using BTCPayServer.Client.Models;

namespace BTCPayServer.Abstractions.Custodians;

public class AssetQuoteUnavailableException : CustodianApiException
{
    public AssetPairData AssetPair { get; }

    public AssetQuoteUnavailableException(AssetPairData assetPair) : base(400, "asset-price-unavailable", "Cannot find a quote for pair " + assetPair)
    {
        this.AssetPair = assetPair;
    }
}
