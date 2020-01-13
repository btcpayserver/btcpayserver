namespace BTCPayServer.Rating
{
    public enum RateSource
    {
        Coingecko,
        CoinAverage,
        Direct
    }
    public class AvailableRateProvider
    {
        public string Name { get; }
        public string Url { get; }
        public string Id { get; }
        public RateSource Source { get; }

        public AvailableRateProvider(string id, string name, string url, RateSource source)
        {
            Id = id;
            Name = name;
            Url = url;
            Source = source;
        }
    }
}
