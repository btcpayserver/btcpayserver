using BTCPayServer.Data;

namespace BTCPayServer.Services.Custodian;

public interface ICanTrade
{

    public TradeResultData tradeMarket();
}



