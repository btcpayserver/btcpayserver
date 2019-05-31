using System;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroLikePaymentData: CryptoPaymentData
    {
        public string GetPaymentId()
        {
            throw new NotImplementedException();
        }

        public string[] GetSearchTerms()
        {
            throw new NotImplementedException();
        }

        public decimal GetValue()
        {
            throw new NotImplementedException();
        }

        public bool PaymentCompleted(PaymentEntity entity, BTCPayNetworkBase network)
        {
            throw new NotImplementedException();
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetworkBase network)
        {
            throw new NotImplementedException();
        }

        public PaymentTypes GetPaymentType()
        {
            throw new NotImplementedException();
        }

        public string GetDestination(BTCPayNetworkBase network)
        {
            return Address;
        }

        public long  Amount { get; set; }
        public string Address { get; set; }
        public long SubaddressIndex { get; set; }
        public long SubaccountIndex { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public string TransactionId { get; set; }
        
    }
}
