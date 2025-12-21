#nullable enable
using System;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class BoltInvoiceClaimDestination(string bolt11, BOLT11PaymentRequest paymentRequest) : ILightningLikeLikeClaimDestination
    {
        public override string ToString()
        {
            return Bolt11;
        }
        public string Bolt11 { get; } = bolt11 ?? throw new ArgumentNullException(nameof(bolt11));
        public BOLT11PaymentRequest PaymentRequest { get; } = paymentRequest;
        public uint256 PaymentHash { get; } = paymentRequest.Hash;
        public string Id => PaymentHash.ToString();
        public decimal? Amount { get; } = paymentRequest.MinimumAmount.MilliSatoshi == LightMoney.Zero ? null:  paymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
    };
}
