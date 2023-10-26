#if ALTCOINS
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Chia.Payments
{
    public class ChiaLikeOnChainPaymentMethodDetails : IPaymentMethodDetails
    {
        public PaymentType GetPaymentType()
        {
            return ChiaPaymentType.Instance;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public decimal GetNextNetworkFee()
        {
            return 0.0m; // The fee is added separately by the user, so it should not be added to the requested sum
        }

        public decimal GetFeeRate()
        {
            return 0.0m;
        }
        public bool Activated { get; set; } = true;
        public int WalletId { get; set; }
        public string DepositAddress { get; set; }
    }
}
#endif
