using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments.AutoTrade
{
    public interface IAutoTradeExchangeClient
    {
        Task<bool> Sell(string cryptoCode, Money amount, decimal? expectedPrice = null);
    }
}
