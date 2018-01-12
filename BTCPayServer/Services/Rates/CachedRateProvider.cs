using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Services.Rates
{
    public class CachedRateProvider : IRateProvider
    {
        private IRateProvider _Inner;
        private IMemoryCache _MemoryCache;
        private string _CryptoCode;

        public CachedRateProvider(string cryptoCode, IRateProvider inner, IMemoryCache memoryCache)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            if (memoryCache == null)
                throw new ArgumentNullException(nameof(memoryCache));
            this._Inner = inner;
            this._MemoryCache = memoryCache;
            this._CryptoCode = cryptoCode;
        }

        public TimeSpan CacheSpan
        {
            get;
            set;
        } = TimeSpan.FromMinutes(1.0);

        public Task<decimal> GetRateAsync(string currency)
        {
            return _MemoryCache.GetOrCreateAsync("CURR_" + currency + "_" + _CryptoCode, (ICacheEntry entry) =>
            {
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                return _Inner.GetRateAsync(currency);
            });
        }
        
        public Task<ICollection<Rate>> GetRatesAsync()
        {
            return _MemoryCache.GetOrCreateAsync("GLOBAL_RATES", (ICacheEntry entry) =>
            {
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                return _Inner.GetRatesAsync();
            });
        }
    }
}
