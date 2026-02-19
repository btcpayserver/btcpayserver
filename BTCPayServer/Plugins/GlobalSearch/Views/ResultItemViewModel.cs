using Newtonsoft.Json;

namespace BTCPayServer.Plugins.GlobalSearch.Views;

public class ResultItemViewModel
{
    [JsonProperty("title")]
    public string Title { get; set; }
    [JsonProperty("subtitle")]
    public string SubTitle { get; set; }
    [JsonProperty("category")]
    public string Category { get; set; }
    [JsonProperty("url")]
    public string Url { get; set; }
    [JsonProperty("keywords")]
    public string[] Keywords { get; set; }
}
