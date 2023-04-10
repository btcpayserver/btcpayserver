using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<StoreRateConfiguration> GetStoreRateConfiguration(string storeId,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/rates/configuration", method: HttpMethod.Get),
                token);
            return await HandleResponse<StoreRateConfiguration>(response);
        }

        public virtual async Task<List<RateSource>> GetRateSources(
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"misc/rate-sources", method: HttpMethod.Get),
                token);
            return await HandleResponse<List<RateSource>>(response);
        }

        public virtual async Task<StoreRateConfiguration> UpdateStoreRateConfiguration(string storeId,
            StoreRateConfiguration request,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/rates/configuration", bodyPayload: request,
                    method: HttpMethod.Put),
                token);
            return await HandleResponse<StoreRateConfiguration>(response);
        }

        public virtual async Task<List<StoreRateResult>> PreviewUpdateStoreRateConfiguration(string storeId,
            StoreRateConfiguration request,
            string[] currencyPair,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/rates/configuration/preview", bodyPayload: request,
                    queryPayload: new Dictionary<string, object>() { { "currencyPair", currencyPair } },
                    method: HttpMethod.Post),
                token);
            return await HandleResponse<List<StoreRateResult>>(response);
        }

        public virtual async Task<List<StoreRateResult>> GetStoreRates(string storeId, string[] currencyPair,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/rates",
                    queryPayload: new Dictionary<string, object>() { { "currencyPair", currencyPair } },
                    method: HttpMethod.Get),
                token);
            return await HandleResponse<List<StoreRateResult>>(response);
        }
    }
}
