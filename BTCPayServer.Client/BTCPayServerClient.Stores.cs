using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<StoreData>> GetStores(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/stores"), token);
            return await HandleResponse<IEnumerable<StoreData>>(response);
        }

        public virtual async Task<StoreData> GetStore(string storeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}"), token);
            return await HandleResponse<StoreData>(response);
        }

        public virtual async Task RemoveStore(string storeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<StoreData> CreateStore(CreateStoreRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/stores", bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<StoreData>(response);
        }

        public virtual async Task<StoreData> UpdateStore(string storeId, UpdateStoreRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}", bodyPayload: request, method: HttpMethod.Put), token);
            return await HandleResponse<StoreData>(response);
        }

        public virtual async Task UpdateStoreAdditionalDataKey(string storeId, string dataKey, JToken data,
            CancellationToken token = default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/data/{dataKey}", bodyPayload: data, method: HttpMethod.Put), token);
            await HandleResponse(response);
        }
        
        public virtual async Task<JToken> GetStoreAdditionalDataKey(string storeId, string dataKey,  CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/data/{dataKey}", method: HttpMethod.Get), token);
            return await HandleResponse<JToken>(response);
        }
        
        public virtual async Task<Dictionary<string, JToken>> GetStoreAdditionalData(string storeId,  CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/data", method: HttpMethod.Get), token);
            return await HandleResponse<Dictionary<string,JToken>>(response);
        }
        public virtual async Task RemoveStoreAdditionalDataKey(string storeId, string dataKey,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/data/{dataKey}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
