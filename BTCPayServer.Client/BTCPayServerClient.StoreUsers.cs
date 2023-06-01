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
        public virtual async Task<List<RoleData>> GetStoreRoles(string storeId,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/roles"), token);
            return await HandleResponse<List<RoleData>>(response);
        }
        
        public virtual async Task<IEnumerable<StoreUserData>> GetStoreUsers(string storeId,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/users"), token);
            return await HandleResponse<IEnumerable<StoreUserData>>(response);
        }

        public virtual async Task RemoveStoreUser(string storeId, string userId, CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/users/{userId}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task AddStoreUser(string storeId, StoreUserData request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/users", bodyPayload: request, method: HttpMethod.Post),
                token);
            await HandleResponse(response);
        }
    }
}
