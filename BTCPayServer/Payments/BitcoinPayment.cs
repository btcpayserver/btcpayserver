using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments
{
    public class BitcoinLikeOnChainPaymentMethod : IPaymentMethod
    {
        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.BTCLike;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress?.ToString();
        }

        public Money GetTxFee()
        {
            return TxFee;
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
            if (newPaymentDestination == null)
                DepositAddress = null;
            else
               DepositAddress = BitcoinAddress.Create(newPaymentDestination, DepositAddress.Network);
        }

        [JsonIgnore]
        public FeeRate FeeRate { get; set; }
        [JsonIgnore]
        public Money TxFee { get; set; }
        [JsonIgnore]
        public BitcoinAddress DepositAddress { get; set; }
    }

    public class BitcoinLikePaymentData : CryptoPaymentData
    {
        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.BTCLike;
        }
        public BitcoinLikePaymentData()
        {

        }
        public BitcoinLikePaymentData(Coin coin, bool rbf)
        {
            Outpoint = coin.Outpoint;
            Output = coin.TxOut;
            ConfirmationCount = 0;
            RBF = rbf;
        }
        [JsonIgnore]
        public OutPoint Outpoint { get; set; }
        [JsonIgnore]
        public TxOut Output { get; set; }
        public int ConfirmationCount { get; set; }
        public bool RBF { get; set; }

        /// <summary>
        /// This is set to true if the payment was created before CryptoPaymentData existed in BTCPayServer
        /// </summary>
        public bool Legacy { get; set; }

        public string GetPaymentId()
        {
            return Outpoint.ToString();
        }

        public string[] GetSearchTerms()
        {
            return new[] { Outpoint.Hash.ToString() };
        }

        public Money GetValue()
        {
            return Output.Value;
        }

        public bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network)
        {
            return ConfirmationCount >= network.MaxTrackedConfirmation;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network)
        {
            if (speedPolicy == SpeedPolicy.HighSpeed)
            {
                return ConfirmationCount >= 1 || !RBF;
            }
            else if (speedPolicy == SpeedPolicy.MediumSpeed)
            {
                return ConfirmationCount >= 1;
            }
            else if (speedPolicy == SpeedPolicy.LowSpeed)
            {
                return ConfirmationCount >= 6;
            }
            return false;
        }
    }
}
