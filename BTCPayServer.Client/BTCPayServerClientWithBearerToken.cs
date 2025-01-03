#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Client.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Client;

public class BTCPayServerClientWithBearerToken : BTCPayServerClient
{
    private const string RefreshPath = "api/v1/bearer/refresh";
    private DateTimeOffset? AccessExpiry { get; set; } // TODO: Incorporate in refresh check
    private string? AccessToken { get; set; }
    private string? RefreshToken { get; set; }

    public event EventHandler<BearerTokenData>? AccessRefreshed;
    
    public BTCPayServerClientWithBearerToken(Uri btcpayHost, HttpClient? httpClient = null) : base(btcpayHost, httpClient)
    {
    }

    public void SetAccess(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessExpiry = expiry;
    }

    public void ClearAccess()
    {
        AccessToken = RefreshToken = null;
        AccessExpiry = null;
    }

    public async Task<BearerTokenData> Login(BearerLoginRequest payload, CancellationToken cancellation = default)
    {
        var now = DateTimeOffset.Now;
        var response = await SendHttpRequest<AccessTokenResponse>("api/v1/bearer/login", payload, HttpMethod.Post, cancellation);
        return HandleAccessTokenResponse(response, now);
    }

    public async Task<BearerTokenData> Login(string loginCode, CancellationToken cancellation = default)
    {
        var now = DateTimeOffset.Now;
        var response = await SendHttpRequest<AccessTokenResponse>("api/v1/bearer/login/code", loginCode, HttpMethod.Post, cancellation);
        return HandleAccessTokenResponse(response, now);
    }

    public async Task<(BearerTokenData? success, string? errorCode)> RefreshAccess(string? refreshToken = null, CancellationToken? cancellation = null)
    {
        var token = refreshToken ?? RefreshToken;
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("No refresh token present or provided.", nameof(refreshToken));

        var payload = new BearerRefreshRequest { RefreshToken = token };
        var now = DateTimeOffset.Now;
        try
        {
            var tokenResponse = await SendHttpRequest<AccessTokenResponse>(RefreshPath, bodyPayload: payload, method: HttpMethod.Post);
            var res = HandleAccessTokenResponse(tokenResponse, now);
            AccessRefreshed?.Invoke(this, res);
            return (res, null);
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }

    protected override HttpRequestMessage CreateHttpRequest(string path, Dictionary<string, object>? queryPayload = null, HttpMethod? method = null)
    {
        var req = base.CreateHttpRequest(path, queryPayload, method);
        req.Headers.Add("Accept", "application/json");
        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return req;
    }

    protected override async Task<T> HandleResponse<T>(HttpResponseMessage res)
    {
        if (res is { IsSuccessStatusCode: false })
        {
            var req = res.RequestMessage;
            if (res.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                // try refresh and recurse if the token could be renewed
                var uri = req!.RequestUri;
                var path = uri!.AbsolutePath;
                if (!path.EndsWith(RefreshPath))
                {
                    var (refresh, _) = await RefreshAccess(RefreshToken);
                    if (refresh != null)
                    {
                        if (req.Content is not null)
                        {
                            var content = await req.Content.ReadAsStringAsync();
                            var payload = JsonConvert.DeserializeObject<T>(content);
                            return await SendHttpRequest<T>(path, bodyPayload: payload, method: req.Method);
                        }

                        var query = HttpUtility.ParseQueryString(uri.Query);
                        var queryPayload = query.HasKeys() ? query.AllKeys.ToDictionary(k => k!, k => query[k]!) : null;
                        return await SendHttpRequest<T>(path, queryPayload, method: req.Method);
                    }
                }
                ClearAccess();
            }
        }
        return await base.HandleResponse<T>(res);
    }

    private BearerTokenData HandleAccessTokenResponse(AccessTokenResponse response, DateTimeOffset expiryOffset)
    {
        var expiry = expiryOffset + TimeSpan.FromSeconds(response.ExpiresIn);
        SetAccess(response.AccessToken, response.RefreshToken, expiry);
        return new BearerTokenData
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            Expiry = expiry
        };
    }
    
    // remodeling Microsoft.AspNetCore.Authentication.BearerToken.AccessTokenResponse
    private class AccessTokenResponse
    {
        public string TokenType { get; } = "Bearer";
        public string AccessToken { get; set; }
        public  string RefreshToken { get; set; }
        public long ExpiresIn { get; set; }
    }
}
