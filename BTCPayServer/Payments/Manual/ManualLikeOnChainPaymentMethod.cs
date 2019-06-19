using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{
    public class ManualPaymentMethod : IPaymentMethodDetails
    {
        public string GetPaymentDestination()
        {
            return null;
        }

        public PaymentType GetPaymentType()
        {
            return PaymentTypes.Manual;
        }

        public decimal GetNextNetworkFee()
        {
            return 0;
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
           
        }
    }
}
