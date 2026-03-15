using System.Collections.Generic;

namespace BTCPayServer.Payouts
{
    public class PayoutMethodHandlerDictionary : HandlersDictionary<PayoutMethodId, IPayoutHandler>
    {
        public PayoutMethodHandlerDictionary(IEnumerable<IPayoutHandler> payoutHandlers) : base(payoutHandlers)
        {
        }
    }
}
