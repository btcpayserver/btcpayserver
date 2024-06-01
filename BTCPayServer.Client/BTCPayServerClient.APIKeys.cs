using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<ApiKeyData> GetCurrentAPIKeyInfo(CancellationToken token = default)
    {
        return await SendHttpRequest<ApiKeyData>("api/v1/api-keys/current", null, HttpMethod.Get, token);
    }

    public virtual async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<ApiKeyData>("api/v1/api-keys", request, HttpMethod.Post, token);
    }

    public virtual async Task<ApiKeyData> CreateAPIKey(string userId, CreateApiKeyRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<ApiKeyData>($"api/v1/users/{userId}/api-keys", request, HttpMethod.Post, token);
    }

    public virtual async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
    {
        await SendHttpRequest("api/v1/api-keys/current", null, HttpMethod.Delete, token);
    }

    public virtual async Task RevokeAPIKey(string apikey, CancellationToken token = default)
    {
        if (apikey == null) throw new ArgumentNullException(nameof(apikey));
        await SendHttpRequest($"api/v1/api-keys/{apikey}", null, HttpMethod.Delete, token);
    }
    public virtual async Task RevokeAPIKey(string userId, string apikey, CancellationToken token = default)
    {
        if (apikey == null) throw new ArgumentNullException(nameof(apikey));
        if (userId is null) throw new ArgumentNullException(nameof(userId));
        await SendHttpRequest($"api/v1/users/{userId}/api-keys/{apikey}", null, HttpMethod.Delete, token);
    }
}
