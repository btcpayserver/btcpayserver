using System;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroLikePaymentData : CryptoPaymentData
    {
        public long Amount { get; set; }
        public string Address { get; set; }
        public long SubaddressIndex { get; set; }
        public long SubaccountIndex { get; set; }
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string TransactionId { get; set; }

        public BTCPayNetworkBase Network { get; set; }

        public string GetPaymentId()
        {
            return $"{TransactionId}#{SubaccountIndex}#{SubaddressIndex}";
        }

        public string[] GetSearchTerms()
        {
            return new[] {TransactionId};
        }

        public decimal GetValue()
        {
            return new LightMoney(Amount).ToDecimal(LightMoneyUnit.BTC);
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return ConfirmationCount >= (Network as MoneroLikeSpecificBtcPayNetwork).MaxTrackedConfirmation;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            switch (speedPolicy)
            {
                case SpeedPolicy.HighSpeed:
                    return ConfirmationCount >= 1;
                case SpeedPolicy.MediumSpeed:
                    return ConfirmationCount >= 5;
                case SpeedPolicy.LowMediumSpeed:
                    return ConfirmationCount >= 7;
                case SpeedPolicy.LowSpeed:
                    return ConfirmationCount >= 10;
                default:
                    return false;
            }
        }

        public PaymentType GetPaymentType()
        {
            return MoneroPaymentType.Instance;
        }

        public string GetDestination()
        {
            return Address;
        }
    }
}
