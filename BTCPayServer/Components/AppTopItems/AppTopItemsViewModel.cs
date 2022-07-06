using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItemsViewModel
{
    public AppData App { get; set; }
    public List<ItemStats> Entries { get; set; }
    public bool InitialRendering { get; set; }
}
