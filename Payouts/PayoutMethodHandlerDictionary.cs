using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BTCPayServer.Payouts
{
    public class PayoutMethodHandlerDictionary : HandlersDictionary<PayoutMethodId, IPayoutHandler>
    {
        public PayoutMethodHandlerDictionary(IEnumerable<IPayoutHandler> payoutHandlers) : base(payoutHandlers)
        {
        }
    }
}
