using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<List<RoleData>> GetStoreRoles(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<List<RoleData>>($"api/v1/stores/{storeId}/roles", null,  HttpMethod.Get,token);
    }
        
    public virtual async Task<IEnumerable<StoreUserData>> GetStoreUsers(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<StoreUserData>>($"api/v1/stores/{storeId}/users", null, HttpMethod.Get, token);
    }

    public virtual async Task RemoveStoreUser(string storeId, string userId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/users/{userId}", null, HttpMethod.Delete, token);
    }

    public virtual async Task AddStoreUser(string storeId, StoreUserData request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        await SendHttpRequest<StoreUserData>($"api/v1/stores/{storeId}/users", request, HttpMethod.Post, token);
    }
}
