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
            public DateTimeOffset Timestamp;
            public Exception Exception;

            internal ExchangeRates GetResult()
            {
                if (Exception != null)
                {
                    ExceptionDispatchInfo.Capture(Exception).Throw();
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

        public DateTimeOffset NextUpdate
        {
            get
            {
                var latest = _Latest;
                if (latest == null || latest.Exception != null)
                    return DateTimeOffset.UtcNow;
                return latest.Timestamp + RefreshRate;
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
            var fetch = new LatestFetch();
            try
            {
                var rates = await _Inner.GetRatesAsync();
                fetch.Latest = rates;
            }
            catch (Exception ex)
            {
                fetch.Exception = ex;
            }
            fetch.Timestamp = DateTimeOffset.UtcNow;
            _Latest = fetch;
            if (fetch.Exception != null)
                ExceptionDispatchInfo.Capture(fetch.Exception).Throw();
            return fetch;
        }

        public void InvalidateCache()
        {
            _Latest = null;
        }
    }
}
