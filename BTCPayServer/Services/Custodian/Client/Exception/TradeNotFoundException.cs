namespace BTCPayServer.Services.Custodian.Client.Exception;

public class TradeNotFoundException : CustodianApiException
{
    private string tradeId { get; }

    public TradeNotFoundException(string tradeId) : base("Could not find trade ID " + tradeId)
    {
        this.tradeId = tradeId;
    }
}
