namespace BTCPayServer.Abstractions.Custodians;

public class TradeNotFoundException : CustodianApiException
{
    private string tradeId { get; }

    public TradeNotFoundException(string tradeId) : base(404, "trade-not-found", "Could not find trade ID " + tradeId)
    {
        this.tradeId = tradeId;
    }
}
