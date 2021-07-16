using System;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class BoltInvoiceClaimDestination : ILightningLikeLikeClaimDestination
    {
        private readonly string _bolt11;
        private readonly decimal _amount;

        public BoltInvoiceClaimDestination(string bolt11, Network network)
        {
            _bolt11 = bolt11 ?? throw new ArgumentNullException(nameof(bolt11));
            _amount = BOLT11PaymentRequest.Parse(bolt11, network).MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
        }

        public BoltInvoiceClaimDestination(string bolt11, BOLT11PaymentRequest invoice)
        {
            _bolt11 = bolt11 ?? throw new ArgumentNullException(nameof(bolt11));
            _amount = invoice?.MinimumAmount.ToDecimal(LightMoneyUnit.BTC) ?? throw new ArgumentNullException(nameof(invoice));
        }

        public override string ToString()
        {
            return _bolt11;
        }

        public decimal? Amount => _amount;
    }
}
