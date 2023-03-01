using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<ApiKeyData> GetCurrentAPIKeyInfo(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/api-keys/current"), token);
            return await HandleResponse<ApiKeyData>(response);
        }

        public virtual async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/api-keys", bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<ApiKeyData>(response);
        }

        public virtual async Task<ApiKeyData> CreateAPIKey(string userId, CreateApiKeyRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/users/{userId}/api-keys",
            bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<ApiKeyData>(response);
        }

        public virtual async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/api-keys/current", null, HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task RevokeAPIKey(string apikey, CancellationToken token = default)
        {
            if (apikey == null)
                throw new ArgumentNullException(nameof(apikey));
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/api-keys/{apikey}", null, HttpMethod.Delete), token);
            await HandleResponse(response);
        }
        public virtual async Task RevokeAPIKey(string userId, string apikey, CancellationToken token = default)
        {
            if (apikey == null)
                throw new ArgumentNullException(nameof(apikey));
            if (userId is null)
                throw new ArgumentNullException(nameof(userId));
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/users/{userId}/api-keys/{apikey}", null, HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
