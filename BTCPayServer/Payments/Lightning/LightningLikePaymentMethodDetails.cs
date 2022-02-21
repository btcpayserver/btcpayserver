using System.Collections.Generic;
using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentMethodDetails : IPaymentMethodDetails
    {
        public string BOLT11 { get; set; }
        public uint256 PaymentHash { get; set; }
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

        public virtual JObject GetAdditionalData()
        {
            return new();
        }
    }
}
