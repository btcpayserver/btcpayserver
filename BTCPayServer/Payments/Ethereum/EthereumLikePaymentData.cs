using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Ethereum
{

    public class EthereumLikePaymentData : CryptoPaymentData
    {
        public string GetPaymentId()
        {
            throw new System.NotImplementedException();
        }

        public string[] GetSearchTerms()
        {
            throw new System.NotImplementedException();
        }

        public decimal GetValue()
        {
            throw new System.NotImplementedException();
        }

        public bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network)
        {
            throw new System.NotImplementedException();
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network)
        {
            throw new System.NotImplementedException();
        }

        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.EthereumLike;
        }
    }
}
