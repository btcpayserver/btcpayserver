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
            var key = GetCacheKey(userId);
            if (_memoryCache.TryGetValue(key, out var code))
            {
                _memoryCache.Remove(code);
                _memoryCache.Remove(key);
            }
            return _memoryCache.GetOrCreate(GetCacheKey(userId), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                var code = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20));
                using var newEntry = _memoryCache.CreateEntry(code);
                newEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                newEntry.Value = userId;

                return code;
            });
        }
        public string Verify(string code)
        {
            if (!_memoryCache.TryGetValue(code, out var userId))
                return null;
            _memoryCache.Remove(GetCacheKey((string)userId));
            _memoryCache.Remove(code);
            return (string)userId;

        }
    }
}
