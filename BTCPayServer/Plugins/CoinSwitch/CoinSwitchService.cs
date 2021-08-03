using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Plugins.CoinSwitch
{
    public class CoinSwitchService: IDisposable
    {
        private readonly StoreRepository _storeRepository;
        private readonly IMemoryCache _memoryCache;

        public CoinSwitchService(StoreRepository storeRepository, IMemoryCache memoryCache)
        {
            _storeRepository = storeRepository;
            _memoryCache = memoryCache;
        }
        
        public async Task<CoinSwitchSettings> GetCoinSwitchForInvoice(string id)
        {
            return await _memoryCache.GetOrCreateAsync($"{nameof(CoinSwitchService)}-{id}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                var d = await _storeRepository.GetStoreByInvoiceId(id);
                
                return d?.GetStoreBlob()?.GetCoinSwitchSettings();
            });
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
