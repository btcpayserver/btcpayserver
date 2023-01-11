using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentMethodDetails : IPaymentMethodDetails
    {
        public string BOLT11 { get; set; }
        public uint256 PaymentHash { get; set; }
        public uint256 Preimage { get; set; }
        public string InvoiceId { get; set; }
        public string NodeInfo { get; set; }

        public virtual string GetPaymentDestination()
        {
            return BOLT11;
        }

        public uint256 GetPaymentHash(Network network)
        {
            return PaymentHash ?? BOLT11PaymentRequest.Parse(BOLT11, network).PaymentHash;
        }

        public virtual PaymentType GetPaymentType()
        {
            return PaymentTypes.LightningLike;
        }

        public decimal GetNextNetworkFee()
        {
            return 0.0m;
        }

        public decimal GetFeeRate()
        {
            return 0.0m;
        }
        public bool Activated { get; set; }

        public virtual string GetAdditionalDataPartialName()
        {
            return null;
        }

        public virtual JObject GetAdditionalData(IEnumerable<PaymentEntity> payments)
        {
            var result = new JObject();
            
            // use set properties and fall back to values from payment data
            var payment = payments.Select(p => p.GetCryptoPaymentData() as LightningLikePaymentData).FirstOrDefault();
            var paymentHash = PaymentHash != null && PaymentHash != default ? PaymentHash : payment?.PaymentHash;
            var preimage = Preimage != null && Preimage != default ? Preimage : payment?.Preimage;
                
            if (paymentHash != null && paymentHash != default)
                result.Add("paymentHash", new JValue(paymentHash.ToString()));
            if (preimage != null && preimage != default)
                result.Add("preimage", new JValue(preimage.ToString()));
                
            return result;
        }
    }
}
