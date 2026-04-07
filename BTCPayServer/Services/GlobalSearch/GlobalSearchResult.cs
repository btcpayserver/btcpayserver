using Newtonsoft.Json;

namespace BTCPayServer.Services.GlobalSearch
{
    public class GlobalSearchResult
    {
        public string Category { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Url { get; set; }

        [JsonIgnore]
        public string Keywords { get; set; }
    }
}
