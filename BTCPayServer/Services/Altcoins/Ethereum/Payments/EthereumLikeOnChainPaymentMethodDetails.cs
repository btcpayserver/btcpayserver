#if ALTCOINS
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Ethereum.Payments
{
    public class EthereumLikeOnChainPaymentMethodDetails : IPaymentMethodDetails
    {
        public PaymentType GetPaymentType()
        {
            return EthereumPaymentType.Instance;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public decimal GetNextNetworkFee()
        {
            return 0;
        }

        public decimal GetFeeRate()
        {
            return 0;
        }

        public void SetPaymentDetails(IPaymentMethodDetails newPaymentMethodDetails)
        {
            throw new System.NotImplementedException();
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
            DepositAddress = newPaymentDestination;
        }

        public bool Activated { get; set; }
        public long Index { get; set; }
        public string XPub { get; set; }
        public string DepositAddress { get; set; }
    }
}
#endif
