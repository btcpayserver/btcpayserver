using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Rates
{
    public class BTCPayRateProviderFactory : IRateProviderFactory
    {
        IMemoryCache _Cache;
        private IOptions<MemoryCacheOptions> _CacheOptions;

        public IMemoryCache Cache
        {
            get
            {
                return _Cache;
            }
        }
        public BTCPayRateProviderFactory(IOptions<MemoryCacheOptions> cacheOptions, IServiceProvider serviceProvider)
        {
            if (cacheOptions == null)
                throw new ArgumentNullException(nameof(cacheOptions));
            _Cache = new MemoryCache(cacheOptions);
            _CacheOptions = cacheOptions;
            // We use 15 min because of limits with free version of bitcoinaverage
            CacheSpan = TimeSpan.FromMinutes(15.0);
            this.serviceProvider = serviceProvider;
        }

        IServiceProvider serviceProvider;
        TimeSpan _CacheSpan;
        public TimeSpan CacheSpan
        {
            get
            {
                return _CacheSpan;
            }
            set
            {
                _CacheSpan = value;
                InvalidateCache();
            }
        }

        public void InvalidateCache()
        {
            _Cache = new MemoryCache(_CacheOptions);
        }

        public IRateProvider GetRateProvider(BTCPayNetwork network)
        {
            return new CachedRateProvider(network.CryptoCode, GetDefaultRateProvider(network), _Cache) { CacheSpan = CacheSpan };
        }

        private IRateProvider GetDefaultRateProvider(BTCPayNetwork network)
        {
            if(network.DefaultRateProvider == null)
            {
                throw new RateUnavailableException(network.CryptoCode);
            }
            return network.DefaultRateProvider.CreateRateProvider(serviceProvider);
        }
    }
}
