#if ALTCOINS
using BTCPayServer.Client.Models;
using BTCPayServer.Common.Altcoins.Chia.Utils;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Altcoins.Chia.Payments
{
    public class ChiaLikePaymentData : CryptoPaymentData
    {
        public ulong Amount { get; set; }
        public string Address { get; set; }
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string TransactionId { get; set; }

        public BTCPayNetworkBase Network { get; set; }

        public string GetPaymentId()
        {
            return $"{TransactionId}";
        }

        public string[] GetSearchTerms()
        {
            return new[] { TransactionId };
        }

        public decimal GetValue()
        {
            return ChiaMoney.Convert(Amount);
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return ConfirmationCount >= (Network as ChiaLikeSpecificBtcPayNetwork).MaxTrackedConfirmation;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            switch (speedPolicy)
            {
                case SpeedPolicy.HighSpeed:
                    return ConfirmationCount >= 0;
                case SpeedPolicy.MediumSpeed:
                    return ConfirmationCount >= 10;
                case SpeedPolicy.LowMediumSpeed:
                    return ConfirmationCount >= 20;
                case SpeedPolicy.LowSpeed:
                    return ConfirmationCount >= 30;
                default:
                    return false;
            }
        }

        public PaymentType GetPaymentType()
        {
            return ChiaPaymentType.Instance;
        }

        public string GetDestination()
        {
            return Address;
        }

        public string GetPaymentProof()
        {
            return null;
        }
    }
}
#endif
