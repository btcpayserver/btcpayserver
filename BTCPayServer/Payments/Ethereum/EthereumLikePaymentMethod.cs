using System;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Ethereum
{
    public class EthereumLikePaymentMethod : IPaymentMethodDetails
    {
        public string GetPaymentDestination()
        {
            throw new NotImplementedException();
        }

        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.EthereumLike;
        }

        public decimal GetTxFee()
        {
            throw new NotImplementedException();
        }

        public void SetNoTxFee()
        {
            throw new NotImplementedException();
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
            throw new NotImplementedException();
        }
    }
}
