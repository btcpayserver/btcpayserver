using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppSales;

public class AppSalesViewModel
{
    public AppData App { get; set; }
    public AppSalesPeriod Period { get; set; } = AppSalesPeriod.Week;
    public int SalesCount { get; set; }
    public IEnumerable<SalesStatsItem> Series { get; set; }
    public bool InitialRendering { get; set; }
}
