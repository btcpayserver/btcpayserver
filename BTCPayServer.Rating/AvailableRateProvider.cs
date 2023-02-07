using System;

namespace BTCPayServer.Rating
{
    public enum RateSource
    {
        Coingecko,
        Direct
    }
    public class AvailableRateProvider
    {
        public string Name { get; }
        public string Url { get; }
        public string Id { get; }
        public RateSource Source { get; }

        public AvailableRateProvider(string id, string name, string url) : this(id, name, url, RateSource.Direct)
        {

        }
        public AvailableRateProvider(string id, string name, string url, RateSource source)
        {
            Id = id;
            Name = name;
            Url = url;
            Source = source;
        }

        public string DisplayName =>
            Source switch
            {
                RateSource.Direct => Name,
                RateSource.Coingecko => $"{Name} (via CoinGecko)",
                _ => throw new NotSupportedException(Source.ToString())
            };
    }
}
