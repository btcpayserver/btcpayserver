using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Rating;
using System.Threading;

namespace BTCPayServer.Services.Rates
{
    public class BackgroundFetcherRateProvider : IRateProvider
    {
        public class LatestFetch
        {
            public ExchangeRates Latest;
            public DateTimeOffset NextRefresh;
            public TimeSpan Backoff = TimeSpan.FromSeconds(5.0); 
            public DateTimeOffset Expiration;
            public Exception Exception;
            public string ExchangeName;
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
                        throw new InvalidOperationException($"The rate has expired ({ExchangeName})");
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

        TimeSpan _RefreshRate = TimeSpan.FromSeconds(30);
        public TimeSpan RefreshRate
        {
            get
            {
                return _RefreshRate;
            }
            set
            {
                var diff = value - _RefreshRate;
                var latest = _Latest;
                if (latest != null)
                    latest.NextRefresh += diff;
                _RefreshRate = value;
            }
        }

        TimeSpan _ValidatyTime = TimeSpan.FromMinutes(10);
        public TimeSpan ValidatyTime
        {
            get
            {
                return _ValidatyTime;
            }
            set
            {
                var diff = value - _ValidatyTime;
                var latest = _Latest;
                if (latest != null)
                    latest.Expiration += diff;
                _ValidatyTime = value;
            }
        }

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

        public bool DoNotAutoFetchIfExpired { get; set; }
        readonly static TimeSpan MaxBackoff = TimeSpan.FromMinutes(5.0);

        public async Task<LatestFetch> UpdateIfNecessary(CancellationToken cancellationToken)
        {
            if (NextUpdate <= DateTimeOffset.UtcNow)
            {
                try
                {
                    await Fetch(cancellationToken);
                }
                catch { } // Exception is inside _Latest
                return _Latest;
            }
            return _Latest;
        }

        LatestFetch _Latest;
        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var latest = _Latest;
            if (!DoNotAutoFetchIfExpired && latest != null && latest.Expiration <= DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0))
            {
                Logs.PayServer.LogWarning($"GetRatesAsync was called on {GetExchangeName()} when the rate is outdated. It should never happen, let BTCPayServer developers know about this.");
                latest = null;
            }
            return (latest ?? (await Fetch(cancellationToken))).GetResult();
        }

        private string GetExchangeName()
        {
            if (_Inner is IHasExchangeName exchangeName)
                return exchangeName.ExchangeName ?? "???";
            return "???";
        }

        private async Task<LatestFetch> Fetch(CancellationToken cancellationToken)
        {
            var previous = _Latest;
            var fetch = new LatestFetch();
            fetch.ExchangeName = GetExchangeName();
            try
            {
                var rates = await _Inner.GetRatesAsync(cancellationToken);
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
                    fetch.Backoff = previous.Backoff * 2;
                    if (fetch.Backoff > MaxBackoff)
                        fetch.Backoff = MaxBackoff;
                }
                else
                {
                    fetch.Expiration = DateTimeOffset.UtcNow;
                }
                fetch.NextRefresh = DateTimeOffset.UtcNow + fetch.Backoff;
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
