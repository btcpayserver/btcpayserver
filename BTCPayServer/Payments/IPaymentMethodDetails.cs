using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments
{
    public interface IPaymentMethodDetails
    {
        string GetPaymentDestination();
        PaymentTypes GetPaymentType();
        Money GetTxFee();
        void SetPaymentDestination(string newPaymentDestination);
    }
}
