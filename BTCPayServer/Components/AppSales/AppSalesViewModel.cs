using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Components.AppSales;

public class AppSalesViewModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string AppType { get; set; }
    public AppSalesPeriod Period { get; set; }
    public string AppUrl { get; set; }
    public string DataUrl { get; set; }
    public long SalesCount { get; set; }
    public IEnumerable<AppSalesStatsItem> Series { get; set; }
    public bool InitialRendering { get; set; }
}
