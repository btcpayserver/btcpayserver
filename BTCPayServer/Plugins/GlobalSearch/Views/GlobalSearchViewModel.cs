using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.GlobalSearch.Views;

public class GlobalSearchViewModel
{
    public List<ResultItemViewModel> Items { get; set; }

    public string GetItemsHash()
    {
        var json = JsonConvert.SerializeObject(this);
        var utf8 = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(utf8).Take(10).ToArray();
        return Convert.ToHexString(hash);
    }

    public string StoreId { get; set; }
    public string RecentKey { get; set; }
    public string SearchUrl { get; set; }
}
public class ResultItemViewModel
{
    public ResultItemViewModel()
    {

    }

    public ResultItemViewModel(ResultItemViewModel other)
    {
        Title = other.Title;
        Category = other.Category;
        Url = other.Url;
        Aliases = other.Aliases?.ToArray();
        Order = other.Order;
    }
    [JsonIgnore]
    public string RequiredPolicy { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }
    public string Url { get; set; }
    public string[] Aliases { get; set; }

    [Obsolete("Use Aliases instead")]
    [JsonIgnore]
    public string[] Keywords
    {
        get => Aliases;
        set => Aliases = value;
    }

    /// <summary>
    /// Lower order values appear first (higher up), and higher order values appear later (lower down).
    /// </summary>
    [JsonIgnore]
    public int Order { get; set; }
}
