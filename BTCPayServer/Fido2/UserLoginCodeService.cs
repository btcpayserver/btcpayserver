using System;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Fido2
{
    public class UserLoginCodeService
    {
        private readonly IMemoryCache _memoryCache;

        public UserLoginCodeService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        private string GetCacheKey(string userId)
        {
            return $"{nameof(UserLoginCodeService)}_{userId.ToLowerInvariant()}";
        }

        public string GetOrGenerate(string userId)
        {
            return _memoryCache.GetOrCreate(GetCacheKey(userId), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return Encoders.Hex.EncodeData(RandomUtils.GetBytes(20));
            });
        }

        public bool Verify(string userId, string code)
        {
            if (!_memoryCache.TryGetValue(GetCacheKey(userId), out var userCode) || !userCode.Equals(code)) return false;
            _memoryCache.Remove(GetCacheKey(userId));
            return true;
        }
    }
}
