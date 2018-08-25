using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class BackgroundFetcherRateProvider : IRateProvider
    {
        public class LatestFetch
        {
            public ExchangeRates Latest;
            public DateTimeOffset NextRefresh;
            public DateTimeOffset Expiration;
            public Exception Exception;

            internal ExchangeRates GetResult()
            {
                if (Expiration <= DateTimeOffset.UtcNow)
                {
                    if (Exception != null)
                    {
                        ExceptionDispatchInfo.Capture(Exception).Throw();
                    }
                    else
                    {
                        throw new InvalidOperationException("The rate has expired");
                    }
                }
                return Latest;
            }
        }

        IRateProvider _Inner;
        public BackgroundFetcherRateProvider(IRateProvider inner)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            _Inner = inner;
        }

        public TimeSpan RefreshRate { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan ValidatyTime { get; set; } = TimeSpan.FromMinutes(10);

        public DateTimeOffset NextUpdate
        {
            get
            {
                var latest = _Latest;
                if (latest == null)
                    return DateTimeOffset.UtcNow;
                return latest.NextRefresh;
            }
        }

        public async Task<LatestFetch> UpdateIfNecessary()
        {
            if (NextUpdate <= DateTimeOffset.UtcNow)
            {
                try
                {
                    await Fetch();
                }
                catch { } // Exception is inside _Latest
                return _Latest;
            }
            return _Latest;
        }

        LatestFetch _Latest;
        public async Task<ExchangeRates> GetRatesAsync()
        {
            return (_Latest ?? (await Fetch())).GetResult();
        }

        private async Task<LatestFetch> Fetch()
        {
            var previous = _Latest;
            var fetch = new LatestFetch();
            try
            {
                var rates = await _Inner.GetRatesAsync();
                fetch.Latest = rates;
                fetch.Expiration = DateTimeOffset.UtcNow + ValidatyTime;
                fetch.NextRefresh = DateTimeOffset.UtcNow + RefreshRate;
            }
            catch (Exception ex)
            {
                if (previous != null)
                {
                    fetch.Latest = previous.Latest;
                    fetch.Expiration = previous.Expiration;
                }
                else
                {
                    fetch.Expiration = DateTimeOffset.UtcNow;
                }
                fetch.NextRefresh = DateTimeOffset.UtcNow;
                fetch.Exception = ex;
            }
            _Latest = fetch;
            fetch.GetResult(); // Will throw if not valid
            return fetch;
        }

        public void InvalidateCache()
        {
            _Latest = null;
        }
    }
}
