using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// This class represent a mode of payment supported by a store.
    /// It is stored at the store level and cloned to the invoice during invoice creation.
    /// This object will be serialized in database in json
    /// </summary>
    public interface ISupportedPaymentMethod
    {
        PaymentMethodId PaymentId { get; }
    }
}
