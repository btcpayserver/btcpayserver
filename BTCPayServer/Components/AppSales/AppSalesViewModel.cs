using System.Collections;
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppSales;

public class AppSalesViewModel
{
    public AppData App { get; set; }
    public IEnumerable<ItemStats> Entries { get; set; }
}
