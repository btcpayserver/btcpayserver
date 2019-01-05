using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentMethodDetails : IPaymentMethodDetails
    {
        public string BOLT11 { get; set; }
        public string InvoiceId { get; set; }
        public string NodeInfo { get; set; }

        public string GetPaymentDestination()
        {
            return BOLT11;
        }

        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.LightningLike;
        }

        public decimal GetNetworkFee()
        {
            return 0.0m;
        }

        public void SetNoNetworkFee()
        {
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
            BOLT11 = newPaymentDestination;
        }
    }
}
