#nullable enable
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<ApplicationUserData> GetCurrentUser(CancellationToken token = default)
    {
        return await SendHttpRequest<ApplicationUserData>("api/v1/users/me", null, HttpMethod.Get, token);
    }

    public virtual async Task<ApplicationUserData> UpdateCurrentUser(UpdateApplicationUserRequest request, CancellationToken token = default)
    {
        return await SendHttpRequest<ApplicationUserData>("api/v1/users/me", request, HttpMethod.Put, token);
    }

    public virtual async Task<ApplicationUserData> UploadCurrentUserProfilePicture(string filePath, string mimeType, CancellationToken token = default)
    {
        return await UploadFileRequest<ApplicationUserData>("api/v1/users/me/picture", filePath, mimeType, "file", HttpMethod.Post, token);
    }

    public virtual async Task DeleteCurrentUserProfilePicture(CancellationToken token = default)
    {
        await SendHttpRequest("api/v1/users/me/picture", null, HttpMethod.Delete, token);
    }
    
    public virtual async Task<ApplicationUserData> CreateUser(CreateApplicationUserRequest request, CancellationToken token = default)
    {
        return await SendHttpRequest<ApplicationUserData>("api/v1/users", request, HttpMethod.Post, token);
    }

    public virtual async Task DeleteUser(string userId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/users/{userId}", null, HttpMethod.Delete, token);
    }

    public virtual async Task<ApplicationUserData> GetUserByIdOrEmail(string idOrEmail, CancellationToken token = default)
    {
        return await SendHttpRequest<ApplicationUserData>($"api/v1/users/{idOrEmail}", null, HttpMethod.Get, token);
    }

    public virtual async Task<bool> LockUser(string idOrEmail, bool locked, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/users/{idOrEmail}/lock", null,
            new LockUserRequest { Locked = locked }, HttpMethod.Post), token);
        await HandleResponse(response);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<bool> ApproveUser(string idOrEmail, bool approved, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/users/{idOrEmail}/approve", null,
            new ApproveUserRequest { Approved = approved }, HttpMethod.Post), token);
        await HandleResponse(response);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<ApplicationUserData[]> GetUsers(CancellationToken token = default)
    {
        return await SendHttpRequest<ApplicationUserData[]>("api/v1/users/", null, HttpMethod.Get, token);
    }

    public virtual async Task DeleteCurrentUser(CancellationToken token = default)
    {
        await DeleteUser("me", token);
    }
}
