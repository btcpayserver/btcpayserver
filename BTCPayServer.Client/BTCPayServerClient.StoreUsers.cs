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

    public virtual async Task<AddStoreUserResult>
        AddStoreUser(string storeId, AddStoreUserDataRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<AddStoreUserResult>($"api/v1/stores/{storeId}/users", request, HttpMethod.Post, token);
    }

    public virtual async Task AcceptStoreInvitation(string invitationToken, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/invitations/{invitationToken}", null, HttpMethod.Post, token);
    }

    public virtual async Task<IEnumerable<StoreInvitationData>> GetStoreInvitations(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<StoreInvitationData>>($"api/v1/stores/{storeId}/users/invitations", null, HttpMethod.Get, token);
    }

    public virtual async Task<StoreInvitationData> GetStoreInvitation(string invitationToken, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreInvitationData>($"api/v1/invitations/{invitationToken}", null, HttpMethod.Get, token);
    }

    public virtual async Task<AddStoreUserResult.InvitationResult> ResendStoreInvitation(string storeId, string userId, CancellationToken token = default)
    {
        return await SendHttpRequest<AddStoreUserResult.InvitationResult>($"api/v1/stores/{storeId}/users/{userId}/invitation", null, HttpMethod.Post, token);
    }

    public virtual async Task CancelStoreInvitation(string storeId, string userId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/users/{userId}/invitation", null, HttpMethod.Delete, token);
    }

    public virtual async Task DeclineStoreInvitation(string invitationToken, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/invitations/{invitationToken}", null, HttpMethod.Delete, token);
    }

    public virtual async Task UpdateStoreUser(string storeId, string userId, StoreUserDataRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        await SendHttpRequest<StoreUserDataRequest>($"api/v1/stores/{storeId}/users/{userId}", request, HttpMethod.Put, token);
    }
}
