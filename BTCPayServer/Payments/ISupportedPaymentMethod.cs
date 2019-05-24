using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// A class for configuration of a type of payment method stored on a store level.
    /// It is cloned to invoices of the store during invoice creation.
    /// This object will be serialized in database in json
    /// </summary>
    public interface ISupportedPaymentMethod
    {
        PaymentMethodId PaymentId { get; }
    }
}
