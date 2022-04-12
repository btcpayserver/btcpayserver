using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoicesViewModel
{
    public StoreData Store { get; set; }
    public IEnumerable<StoreRecentInvoiceViewModel> Invoices { get; set; }
}
