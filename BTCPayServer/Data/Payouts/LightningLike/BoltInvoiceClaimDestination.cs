using System;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class BoltInvoiceClaimDestination : ILightningLikeLikeClaimDestination
    {
        private readonly string _bolt11;

        public BoltInvoiceClaimDestination(string bolt11)
        {
            _bolt11 = bolt11 ?? throw new ArgumentNullException(nameof(bolt11));
        }

        public override string ToString()
        {
            return _bolt11;
        }
    }
}
