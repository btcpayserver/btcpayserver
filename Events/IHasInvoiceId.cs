using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public interface IHasInvoiceId
    {
        string InvoiceId { get; }
    }
}
