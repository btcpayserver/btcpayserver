using System.Collections.Generic;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItemsViewModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string AppType { get; set; }
    public string AppUrl { get; set; }
    public string DataUrl { get; set; }
    public List<ItemStats> Entries { get; set; }
    public List<int> SalesCount { get; set; }
    public bool InitialRendering { get; set; }
}
