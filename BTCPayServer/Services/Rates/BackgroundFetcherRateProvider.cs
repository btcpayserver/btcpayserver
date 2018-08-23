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
            public DateTimeOffset Expiration;
            public Exception Exception;

            internal ExchangeRates GetResult()
            {
                if(Expiration < DateTimeOffset.UtcNow)
                {
                    if(Exception != null)
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

            internal void CopyFrom(LatestFetch previous)
            {
                Latest = previous.Latest;
                Timestamp = previous.Timestamp;
                Expiration = previous.Expiration;
                Exception = previous.Exception;
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
            var previous = _Latest;
            var fetch = new LatestFetch();
            try
            {
                var rates = await _Inner.GetRatesAsync();
                fetch.Latest = rates;
                fetch.Expiration = DateTimeOffset.UtcNow + ValidatyTime;
            }
            catch (Exception ex)
            {
                if(previous != null)
                {
                    fetch.CopyFrom(previous);
                }
                fetch.Exception = ex;
            }
            fetch.Timestamp = DateTimeOffset.UtcNow;
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
