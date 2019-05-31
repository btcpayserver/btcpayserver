namespace BTCPayServer.Payments.Monero
{
    public class MoneroLikeOnChainPaymentMethodDetails : IPaymentMethodDetails
    {
        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.MoneroLike;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public decimal GetNextNetworkFee()
        {
            return NextNetworkFee;
        }
        public void SetPaymentDestination(string newPaymentDestination)
        {
            DepositAddress = newPaymentDestination;
        }
        public int AccountIndex { get; set; }
        public long AddressIndex { get; set; }
        public string DepositAddress { get; set; }
        public decimal NextNetworkFee { get; set; }
    }
}