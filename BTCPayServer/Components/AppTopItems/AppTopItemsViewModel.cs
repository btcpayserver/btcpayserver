using System.Collections;
using BTCPayServer.Data;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItemsViewModel
{
    public AppData App { get; set; }
    public IEnumerable Entries { get; set; }
}
