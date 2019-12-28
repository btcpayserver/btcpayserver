using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
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
            _Inner = inner;
            MemoryCache = memoryCache;
            ExchangeName = exchangeName;
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
        
        public Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            return MemoryCache.GetOrCreateAsync("EXCHANGE_RATES_" + ExchangeName, entry =>
            {
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                return _Inner.GetRatesAsync(cancellationToken);
            });
        }
    }
}
