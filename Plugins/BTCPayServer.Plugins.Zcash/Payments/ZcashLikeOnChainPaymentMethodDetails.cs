
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Zcash.Payments
{
    public class ZcashLikeOnChainPaymentMethodDetails : IPaymentMethodDetails
    {
        public PaymentType GetPaymentType()
        {
            return ZcashPaymentType.Instance;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public decimal GetNextNetworkFee()
        {
            return NextNetworkFee;
        }

        public decimal GetFeeRate()
        {
            return 0.0m;
        }
        public bool Activated { get; set; } = true;
        public long AccountIndex { get; set; }
        public long AddressIndex { get; set; }
        public string DepositAddress { get; set; }
        public decimal NextNetworkFee { get; set; }
    }
}

