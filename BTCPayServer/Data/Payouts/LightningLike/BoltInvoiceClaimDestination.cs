using System;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class BoltInvoiceClaimDestination : ILightningLikeLikeClaimDestination
    {
        public BoltInvoiceClaimDestination(string bolt11, BOLT11PaymentRequest paymentRequest)
        {
            Bolt11 = bolt11 ?? throw new ArgumentNullException(nameof(bolt11));
            PaymentRequest = paymentRequest;
            PaymentHash = paymentRequest.Hash;
            Amount =  paymentRequest.MinimumAmount.MilliSatoshi == LightMoney.Zero ? null:  paymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
        }

        public override string ToString()
        {
            return Bolt11;
        }
        public string Bolt11 { get; }
        public BOLT11PaymentRequest PaymentRequest { get; }
        public uint256 PaymentHash { get; }
        public string Id => PaymentHash.ToString();
        public decimal? Amount { get; }
        public bool IsExplicitAmountMinimum => true;
    }
}
