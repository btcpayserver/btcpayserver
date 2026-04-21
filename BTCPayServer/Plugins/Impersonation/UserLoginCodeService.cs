#nullable enable
using System;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.Impersonation;

public class UserLoginCodeService(IMemoryCache memoryCache)
{
    public static readonly TimeSpan ExpirationTime = TimeSpan.FromSeconds(60);

    private string CacheKey(string code) => $"{nameof(UserLoginCodeService)}_{code.ToLowerInvariant()}";

    public string Generate(string userId)
    {
        var code = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20));
        memoryCache.Set(CacheKey(code), userId, ExpirationTime);
        return code;
    }

    public string? Verify(string code)
    {
        var key = CacheKey(code);
        if (!memoryCache.TryGetValue(key, out var o) || o is not string userId)
            return null;
        memoryCache.Remove(key);
        return userId;
    }
}
