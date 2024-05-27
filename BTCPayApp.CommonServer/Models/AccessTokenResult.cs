using System;

namespace BTCPayApp.CommonServer.Models;

public class AccessTokenResult(string accessToken, string refreshToken, DateTimeOffset expiry)
{
    public string AccessToken { get; init; } = accessToken;
    public string RefreshToken { get; init; } = refreshToken;
    public DateTimeOffset Expiry { get; init; } = expiry;
}
