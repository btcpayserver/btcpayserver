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
        public string SourceId { get; }
        public RateSource Source { get; }

        public AvailableRateProvider(string id, string name, string url) : this(id, id, name, url, RateSource.Direct)
        {

        }
        public AvailableRateProvider(string id, string sourceId, string name, string url, RateSource source)
        {
            Id = id;
            SourceId = sourceId;
            Name = name;
            Url = url;
            Source = source;
        }
    }
}
