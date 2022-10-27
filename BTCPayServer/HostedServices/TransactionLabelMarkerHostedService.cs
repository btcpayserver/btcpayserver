#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.PaymentRequests;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly EventAggregator _eventAggregator;
        private readonly WalletRepository _walletRepository;

        public TransactionLabelMarkerHostedService(EventAggregator eventAggregator, WalletRepository walletRepository, Logs logs) :
            base(eventAggregator, logs)
        {
            _eventAggregator = eventAggregator;
            _walletRepository = walletRepository;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
        }
        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent && invoiceEvent.Name == InvoiceEvent.ReceivedPayment &&
                invoiceEvent.Payment.GetPaymentMethodId()?.PaymentType == BitcoinPaymentType.Instance &&
                invoiceEvent.Payment.GetCryptoPaymentData() is BitcoinLikePaymentData bitcoinLikePaymentData)
            {
                var walletId = new WalletId(invoiceEvent.Invoice.StoreId, invoiceEvent.Payment.GetCryptoCode());
                var transactionId = bitcoinLikePaymentData.Outpoint.Hash;
                var labels = new List<Attachment>
                {
                    Attachment.Invoice(invoiceEvent.Invoice.Id)
                };
                foreach (var paymentId in PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(Attachment.PaymentRequest(paymentId));
                }
                foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(Attachment.App(appId));
                }

                await _walletRepository.AddWalletTransactionAttachment(walletId, transactionId, labels);
            }
        }
    }
}
