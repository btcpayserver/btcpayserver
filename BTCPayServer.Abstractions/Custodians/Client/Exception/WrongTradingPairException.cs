namespace BTCPayServer.Abstractions.Custodians;

public class WrongTradingPairException : CustodianApiException
{
    public const int HttpCode = 404;
    public WrongTradingPairException(string fromAsset, string toAsset) : base(HttpCode, "wrong-trading-pair", $"Cannot find a trading pair for converting {fromAsset} into {toAsset}.")
    {
    }
}
