using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<StoreRateConfiguration> GetStoreRateConfiguration(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreRateConfiguration>($"api/v1/stores/{storeId}/rates/configuration", null, HttpMethod.Get, token);
    }

    public virtual async Task<List<RateSource>> GetRateSources(CancellationToken token = default)
    {
        return await SendHttpRequest<List<RateSource>>("misc/rate-sources", null, HttpMethod.Get, token);
    }

    public virtual async Task<StoreRateConfiguration> UpdateStoreRateConfiguration(string storeId, StoreRateConfiguration request, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreRateConfiguration>($"api/v1/stores/{storeId}/rates/configuration", request, HttpMethod.Put, token);
    }

    public virtual async Task<List<StoreRateResult>> PreviewUpdateStoreRateConfiguration(string storeId, StoreRateConfiguration request, string[] currencyPair, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreRateConfiguration, List<StoreRateResult>>($"api/v1/stores/{storeId}/rates/configuration/preview", 
            new Dictionary<string, object> { { "currencyPair", currencyPair } }, request, HttpMethod.Post, token);
    }

    public virtual async Task<List<StoreRateResult>> GetStoreRates(string storeId, string[] currencyPair, CancellationToken token = default)
    {
        return await SendHttpRequest<List<StoreRateResult>>($"api/v1/stores/{storeId}/rates", new Dictionary<string, object>() { { "currencyPair", currencyPair } }, HttpMethod.Get, token);
    }
}
