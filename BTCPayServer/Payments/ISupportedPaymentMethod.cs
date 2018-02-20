using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    public interface ISupportedPaymentMethod
    {
        PaymentMethodId PaymentId { get; }
    }
}
