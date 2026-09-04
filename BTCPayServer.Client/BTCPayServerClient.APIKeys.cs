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
        return await SendHttpRequest<ApiKeyData>($"api/v1/api-keys/current", null, HttpMethod.Get, token);
    }

    public virtual async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<ApiKeyData>($"api/v1/api-keys", request, HttpMethod.Post, token);
    }

    public virtual async Task<ApiKeyData> CreateAPIKey(string userId, CreateApiKeyRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<ApiKeyData>($"api/v1/users/{userId}/api-keys", request, HttpMethod.Post, token);
    }

    public virtual async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/api-keys/current", null, HttpMethod.Delete, token);
    }

    public virtual async Task RevokeAPIKey(string apiKeyId, CancellationToken token = default)
    {
        if (apiKeyId == null) throw new ArgumentNullException(nameof(apiKeyId));
        await SendHttpRequest($"api/v1/api-keys/{apiKeyId}", null, HttpMethod.Delete, token);
    }
    public virtual async Task RevokeAPIKey(string userId, string apiKeyId, CancellationToken token = default)
    {
        if (apiKeyId == null) throw new ArgumentNullException(nameof(apiKeyId));
        if (userId is null) throw new ArgumentNullException(nameof(userId));
        await SendHttpRequest($"api/v1/users/{userId}/api-keys/{apiKeyId}", null, HttpMethod.Delete, token);
    }
}
