using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBitcoin.BIP78.Client;
using NBitcoin.BIP78.Server;

namespace BTCPayServer.Payments.PayJoin
{
    public class BTCPayPayjoinProposalContext: PayjoinProposalContext
    {
        public InvoiceEntity Invoice { get; set; }
        public InvoiceLogs InvoiceLogs { get; set; } = new InvoiceLogs();

        public BTCPayPayjoinProposalContext(PSBT originalPSBT, PayjoinClientParameters payjoinClientParameters = null) : base(originalPSBT, payjoinClientParameters)
        {
        }
    }

    public class BTCPayPayjoinReceiverWallet : PayjoinReceiverWallet<BTCPayPayjoinProposalContext>
    {
        private readonly PayJoinRepository _payJoinRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly DelayedTransactionBroadcaster _broadcaster;

        public BTCPayPayjoinReceiverWallet(PayJoinRepository payJoinRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            ExplorerClientProvider explorerClientProvider,
            InvoiceRepository invoiceRepository,
            DelayedTransactionBroadcaster broadcaster)
        {
            _payJoinRepository = payJoinRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _explorerClientProvider = explorerClientProvider;
            _invoiceRepository = invoiceRepository;
            _broadcaster = broadcaster;
        }

        protected override Task<bool> SupportsType(ScriptPubKeyType scriptPubKeyType)
        {
            return Task.FromResult(scriptPubKeyType != ScriptPubKeyType.Legacy);
        }

        protected override Task<bool> InputsSeenBefore(PSBTInputList inputList)
        {
            return _payJoinRepository.TryLockInputs(inputList.Select(input => input.PrevOut).ToArray());
        }

        protected override async Task<string> IsMempoolEligible(PSBT psbt)
        {
            var explorerClient = _explorerClientProvider.GetExplorerClient(psbt.Network.NetworkSet.CryptoCode);
            var result = await explorerClient.BroadcastAsync(psbt.ExtractTransaction(), true);
            return result.Success ? null : result.RPCCodeMessage;
        }

        protected override async Task SchedulePSBTBroadcast(PSBT psbt)
        {
            await _broadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2.0), psbt.ExtractTransaction(),
                _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(psbt.Network.NetworkSet.CryptoCode));
        }

        protected override async Task ComputePayjoinModifications(BTCPayPayjoinProposalContext context)
        {
            
            if(context.PayjoinReceiverWalletProposal is null)
                await _payJoinRepository.TryUnlock(context.OriginalPSBT.Inputs.Select(input => input.PrevOut).ToArray());
        }

        protected override async Task<PayjoinPaymentRequest> FindMatchingPaymentRequests(
            BTCPayPayjoinProposalContext context)
        {
            foreach (var output in context.OriginalPSBT.Outputs)
            {
                var paymentMethodId = new PaymentMethodId(context.OriginalPSBT.Network.NetworkSet.CryptoCode.ToUpperInvariant(),
                    BitcoinPaymentType.Instance);
                var key = output.ScriptPubKey.Hash + "#" + paymentMethodId.CryptoCode;
                var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {key})).FirstOrDefault();
                var paymentMethod = invoice?.GetPaymentMethod(paymentMethodId);
                if (paymentMethod?.GetPaymentMethodDetails() is BitcoinLikeOnChainPaymentMethod paymentMethodDetails)
                {
                    context.Invoice = invoice;
                    return new PayjoinPaymentRequest
                    {
                        Amount = paymentMethod.Calculate().Due,
                        Destination = paymentMethodDetails.GetDepositAddress(context.OriginalPSBT.Network)
                    };
                }
            }

            return null;
        }
    }
}
