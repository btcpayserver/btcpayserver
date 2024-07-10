using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class AppSalesStats
{
    public int SalesCount { get; set; }
    public IEnumerable<AppSalesStatsItem> Series { get; set; }
}

public class AppSalesStatsItem
{
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTime Date { get; set; }
    public string Label { get; set; }
    public int SalesCount { get; set; }
}
