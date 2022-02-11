namespace BTCPayServer.Services.Custodian.Client.Exception;

public class WrongTradingPairException: CustodianApiException
{
    public WrongTradingPairException(string fromAsset, string toAsset) : base(400, "wrong-trading-pair", $"Cannot find a trading pair for converting {fromAsset} into {toAsset}.")
    {
    }
}
