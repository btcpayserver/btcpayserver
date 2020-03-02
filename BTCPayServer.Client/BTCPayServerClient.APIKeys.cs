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
        
        public virtual async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/api-keys/current", null, HttpMethod.Delete), token);
            HandleResponse(response);
        }
    }
}
