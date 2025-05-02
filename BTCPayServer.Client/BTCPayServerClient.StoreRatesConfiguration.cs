using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<StoreRateConfiguration> GetStoreRateConfiguration(string storeId, bool? fallback = null, CancellationToken token = default)
    {
        var path = GetRateConfigPath(storeId, fallback);
        return await SendHttpRequest<StoreRateConfiguration>(path, null, HttpMethod.Get, token);
    }

    private string GetRateConfigPath(string storeId, bool? fallback)
    => fallback switch
    {
        null => $"api/v1/stores/{storeId}/rates/configuration",
        true => $"api/v1/stores/{storeId}/rates/configuration/fallback",
        false => $"api/v1/stores/{storeId}/rates/configuration/primary",
    };

    public virtual async Task<List<RateSource>> GetRateSources(CancellationToken token = default)
    {
        return await SendHttpRequest<List<RateSource>>("misc/rate-sources", null, HttpMethod.Get, token);
    }

    public virtual async Task<StoreRateConfiguration> UpdateStoreRateConfiguration(string storeId, StoreRateConfiguration request, bool? fallback = null, CancellationToken token = default)
    {
        var path = GetRateConfigPath(storeId, fallback);
        return await SendHttpRequest<StoreRateConfiguration>(path, request, HttpMethod.Put, token);
    }

    public virtual async Task<List<StoreRateResult>> PreviewUpdateStoreRateConfiguration(string storeId, StoreRateConfiguration request, string[] currencyPair = null, CancellationToken token = default)
    {
        var queryPayload = currencyPair == null ? null : new Dictionary<string, object> { { "currencyPair", currencyPair } };
        return await SendHttpRequest<StoreRateConfiguration, List<StoreRateResult>>($"api/v1/stores/{storeId}/rates/configuration/preview", queryPayload, request, HttpMethod.Post, token);
    }

    public virtual async Task<List<StoreRateResult>> GetStoreRates(string storeId, string[] currencyPair = null, CancellationToken token = default)
    {
        var queryPayload = currencyPair == null ? null : new Dictionary<string, object> { { "currencyPair", currencyPair } };
        return await SendHttpRequest<List<StoreRateResult>>($"api/v1/stores/{storeId}/rates", queryPayload, HttpMethod.Get, token);
    }
}
