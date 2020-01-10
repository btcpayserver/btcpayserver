namespace BTCPayServer.Rating
{
    public class AvailableRateProvider
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Id { get; set; }

        public AvailableRateProvider(string id, string name, string url)
        {
            Id = id;
            Name = name;
            Url = url;
        }
    }
}
