using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.GlobalSearch.Views;

public class GlobalSearchViewModel
{
    public List<ResultItemViewModel> Items { get; set; }
    public string StoreId { get; set; }
    public string SearchUrl { get; set; }
}
public class ResultItemViewModel
{
    public string Title { get; set; }
    public string SubTitle { get; set; }
    public string Category { get; set; }
    public string Url { get; set; }
    public string[] Keywords { get; set; }
}


