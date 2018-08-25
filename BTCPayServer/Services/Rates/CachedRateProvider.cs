using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Services.Rates
{
    public class CachedRateProvider : IRateProvider, IHasExchangeName
    {
        private IRateProvider _Inner;
        private IMemoryCache _MemoryCache;

        public CachedRateProvider(string exchangeName, IRateProvider inner, IMemoryCache memoryCache)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            if (memoryCache == null)
                throw new ArgumentNullException(nameof(memoryCache));
            this._Inner = inner;
            this.MemoryCache = memoryCache;
            this.ExchangeName = exchangeName;
        }

        public IRateProvider Inner
        {
            get
            {
                return _Inner;
            }
        }

        public string ExchangeName { get; }

        public TimeSpan CacheSpan
        {
            get;
            set;
        } = TimeSpan.FromMinutes(1.0);
        public IMemoryCache MemoryCache { get => _MemoryCache; set => _MemoryCache = value; }
        
        public Task<ExchangeRates> GetRatesAsync()
        {
            return MemoryCache.GetOrCreateAsync("EXCHANGE_RATES_" + ExchangeName, (ICacheEntry entry) =>
            {
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                return _Inner.GetRatesAsync();
            });
        }
    }
}
