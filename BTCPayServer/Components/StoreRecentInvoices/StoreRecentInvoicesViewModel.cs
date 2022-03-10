using System.Collections;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoicesViewModel
{
    public StoreData Store { get; set; }
    public IEnumerable Entries { get; set; }
}
