using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Custodians.Client;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Custodians;

public interface ICanTrade
{
    /**
     * A list of tradable asset pairs, or NULL if the custodian cannot trade/convert assets. if thr asset pair contains fiat, fiat is always put last. If both assets are a cyrptocode or both are fiat, the pair is written alphabetically. Always in uppercase. Example: ["BTC/EUR","BTC/USD", "EUR/USD", "BTC/ETH",...]
     */
    public List<AssetPairData> GetTradableAssetPairs();

    /**
     * Execute a market order right now.
     */
    public Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config, CancellationToken cancellationToken);

    /**
     * Get the details about a previous market trade.
     */
    public Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config, CancellationToken cancellationToken);

    public Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config, CancellationToken cancellationToken);
}



