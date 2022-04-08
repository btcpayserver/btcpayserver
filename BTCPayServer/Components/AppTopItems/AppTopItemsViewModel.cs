using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItemsViewModel
{
    public AppData App { get; set; }
    public IEnumerable<ItemStats> Entries { get; set; }
}
