using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Controllers;

public class BtcPayAppService
{
    private readonly IMemoryCache _memoryCache;

    public BtcPayAppService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    private string CacheKey(string k) => $"BtcPayAppService_{k}";

    public async Task<string> GeneratePairingCode(string storeId, string userId)
    {
        var code = Guid.NewGuid().ToString();
        _memoryCache.Set(CacheKey(code), new PairingRequest() {Key = code, StoreId = storeId, UserId = userId},
            TimeSpan.FromMinutes(5));
        return code;
    }

    public PairingRequest? ConsumePairingCode(string code)
    {
        return _memoryCache.TryGetValue(CacheKey(code), out var pairingRequest)
            ? (PairingRequest?)pairingRequest
            : null;
    }

    public class PairingRequest
    {
        public string Key { get; set; }
        public string StoreId { get; set; }
        public string UserId { get; set; }
    }
}