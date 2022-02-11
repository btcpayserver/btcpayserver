using System.Threading.Tasks;
using BTCPayServer.Services.Custodian.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian;

public interface ICanTrade
{

    /**
     * Execute a market order right now.
     */
    public Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config);
    
    /**
     * Get the details about a previous market trade.
     */
    public Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config);

    public Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config);
}



