using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian.Client;

public interface ICustodian
{
    /**
     * Get the unique code that identifies this custodian.
     */
    public string GetCode();
    
    public string GetName();

    /**
     * Get a list of assets (cryptoCode or currencyCode) the custodian can store. Always in uppercase.
     * Example: ["BTC","EUR", "USD", "ETH",...]
     */
    public string[] GetSupportedAssets();

    /**
     * A list of tradable asset pairs, or NULL if the custodian cannot trade/convert assets. if thr asset pair contains fiat, fiat is always put last. If both assets are a cyrptocode or both are fiat, the pair is written alphabetically. Always in uppercase. Example: ["BTC/EUR","BTC/USD", "EUR/USD", "BTC/ETH",...]
     */
    public List<AssetPairData> GetTradableAssetPairs();

    /**
     * Get a list of assets and their qty in custody.
     */
    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config);
}
