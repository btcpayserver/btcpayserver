using BIP78.Receiver;
using BIP78.Sender;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin.Receiver
{
    public class BTCPayPayjoinProposalContext : PayjoinProposalContext
    {
        public BTCPayNetwork Network { get; set; }
        public InvoiceEntity Invoice { get; set; }
        public InvoiceLogs InvoiceLogs { get; set; } = new InvoiceLogs();
        public DerivationSchemeSettings PaymentMethod { get; set; }
        public ExtKey SigningKey { get; set; }
        public BitcoinLikeOnChainPaymentMethod PaymentMethodDetails { get; set; }

        public BTCPayPayjoinProposalContext(PSBT originalPSBT, BTCPayNetwork network,
            PayjoinClientParameters payjoinClientParameters = null) : base(originalPSBT, payjoinClientParameters)
        {
            Network = network;
        }
    }
}
