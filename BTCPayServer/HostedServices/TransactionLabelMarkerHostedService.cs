using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly WalletRepository _walletRepository;

        public TransactionLabelMarkerHostedService(EventAggregator eventAggregator, WalletRepository walletRepository) :
            base(eventAggregator)
        {
            _walletRepository = walletRepository;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            _ = StartLoop();

        }

        private async Task StartLoop()
        {
            while (!_Cts.IsCancellationRequested)
            {
                await _walletRepository.ProcessLabels();
                await Task.Delay(1000, _Cts.Token);
            }
        }

        protected override Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent && invoiceEvent.Name == InvoiceEvent.ReceivedPayment && 
                invoiceEvent.Payment.GetPaymentMethodId().PaymentType == BitcoinPaymentType.Instance && 
                invoiceEvent.Payment.GetCryptoPaymentData() is BitcoinLikePaymentData bitcoinLikePaymentData)
            {
                var payjoinLabelColor = "#51b13e";
                var invoiceLabelColor = "#0f3b21";
                var walletId = new WalletId(invoiceEvent.Invoice.StoreId, invoiceEvent.Payment.GetCryptoCode());
                var transactionId = bitcoinLikePaymentData.Outpoint.Hash;
                var labels = new List<(string color, string label)> {(invoiceLabelColor, $"invoice-{invoiceEvent.Invoice.Id}")};

                if (invoiceEvent.Invoice.GetPayments(invoiceEvent.Payment.GetCryptoCode()).Any(entity =>
                    entity.GetCryptoPaymentData() is BitcoinLikePaymentData pData &&
                    pData.PayjoinInformation?.CoinjoinTransactionHash == transactionId))
                {
                    labels.Add((payjoinLabelColor, "payjoin"));
                }

                _walletRepository.AddLabels(walletId,
                    new Dictionary<uint256, List<(string color, string label)>>() {{transactionId, labels}});
            }
            return Task.CompletedTask;
        }

    }
}
