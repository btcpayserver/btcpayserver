using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppSales;

public class AppSalesViewModel
{
    public AppData App { get; set; }
    public int SalesCount { get; set; }
    public IEnumerable<SalesStatsItem> Series { get; set; }
}
