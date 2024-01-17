using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Rates
{
    public class BackgroundFetcherState
    {
        public string ExchangeName { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? LastRequested { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? LastUpdated { get; set; }
        [JsonProperty(ItemConverterType = typeof(BackgroundFetcherRateJsonConverter))]
        public List<BackgroundFetcherRate> Rates { get; set; }
    }
    public class BackgroundFetcherRate
    {
        public CurrencyPair Pair { get; set; }
        public BidAsk BidAsk { get; set; }
    }
    //This make the json more compact
    class BackgroundFetcherRateJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(BackgroundFetcherRate).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            var parts = value.Split('|');
            return new BackgroundFetcherRate()
            {
                Pair = CurrencyPair.Parse(parts[0]),
                BidAsk = new BidAsk(decimal.Parse(parts[1], CultureInfo.InvariantCulture), decimal.Parse(parts[2], CultureInfo.InvariantCulture))
            };
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var rate = (BackgroundFetcherRate)value;
            writer.WriteValue($"{rate.Pair}|{rate.BidAsk.Bid.ToString(CultureInfo.InvariantCulture)}|{rate.BidAsk.Ask.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>
    /// This class is a decorator which handle caching and pre-emptive query to the underlying rate provider
    /// </summary>
    public class BackgroundFetcherRateProvider : IRateProvider
    {
        public class LatestFetch
        {
            public PairRate[] Latest;
            public DateTimeOffset NextRefresh;
            public TimeSpan Backoff = TimeSpan.FromSeconds(5.0);
            public DateTimeOffset Updated;
            public DateTimeOffset Expiration;
            public Exception Exception;
            internal PairRate[] GetResult()
            {
                if (Expiration <= DateTimeOffset.UtcNow)
                {
                    if (Exception != null)
                    {
                        ExceptionDispatchInfo.Capture(Exception).Throw();
                    }
                    else
                    {
                        throw new InvalidOperationException($"The rate has expired");
                    }
                }
                return Latest;
            }
        }

        readonly IRateProvider _Inner;
        public IRateProvider Inner => _Inner;

        public BackgroundFetcherRateProvider(IRateProvider inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _Inner = inner;
        }

        public BackgroundFetcherState GetState()
        {
            var state = new BackgroundFetcherState()
            {
                LastRequested = LastRequested
            };
            if (_Latest is LatestFetch fetch && fetch.Latest is not null)
            {
                state.LastUpdated = fetch.Updated;
                state.Rates = fetch.Latest
                            .Select(r => new BackgroundFetcherRate()
                            {
                                Pair = r.CurrencyPair,
                                BidAsk = r.BidAsk
                            }).ToList();
            }
            return state;
        }

        public void LoadState(BackgroundFetcherState state)
        {
            if (state.LastRequested is DateTimeOffset lastRequested)
                this.LastRequested = state.LastRequested;
            if (state.LastUpdated is DateTimeOffset updated && state.Rates is List<BackgroundFetcherRate> rates)
            {
                var fetch = new LatestFetch()
                {
                    Latest = rates.Select(r => new PairRate(r.Pair, r.BidAsk)).ToArray(),
                    Updated = updated,
                    NextRefresh = updated + RefreshRate,
                    Expiration = updated + ValidatyTime
                };
                _Latest = fetch;
            }
        }

        TimeSpan _RefreshRate = TimeSpan.FromSeconds(30);
        /// <summary>
        /// The timespan after which <see cref="UpdateIfNecessary(CancellationToken)"/> will get the rates from the underlying rate provider
        /// </summary>
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
        /// <summary>
        /// The timespan after which calls to <see cref="GetRatesAsync(CancellationToken)"/> will query underlying provider if the rate has not been updated
        /// </summary>
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
        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            LastRequested = DateTimeOffset.UtcNow;
            var latest = _Latest;
            if (!DoNotAutoFetchIfExpired && latest != null && latest.Expiration <= DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0))
            {
                latest = null;
            }
            return (latest ?? (await Fetch(cancellationToken))).GetResult();
        }

        /// <summary>
        /// The last time this rate provider has been used
        /// </summary>
        public DateTimeOffset? LastRequested { get; set; }

        public DateTimeOffset? Expiration
        {
            get
            {
                if (_Latest is LatestFetch f)
                {
                    return f.Expiration;
                }
                return null;
            }
        }

        public RateSourceInfo RateSourceInfo => _Inner.RateSourceInfo;

        private async Task<LatestFetch> Fetch(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = _Latest;
            var fetch = new LatestFetch();
            try
            {
                var rates = await _Inner.GetRatesAsync(cancellationToken);
                fetch.Latest = rates;
                fetch.Updated = DateTimeOffset.UtcNow;
                fetch.Expiration = fetch.Updated + ValidatyTime;
                fetch.NextRefresh = fetch.Updated + RefreshRate;
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
