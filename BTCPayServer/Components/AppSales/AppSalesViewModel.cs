using System.Collections;
using BTCPayServer.Data;

namespace BTCPayServer.Components.AppSales;

public class AppSalesViewModel
{
    public AppData App { get; set; }
    public IEnumerable Entries { get; set; }
}
