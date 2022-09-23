using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.PaymentRequests;
using NBitcoin;

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
            Subscribe<NewOnChainTransactionEvent>();
            Subscribe<UpdateTransactionLabel>();
        }
        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent && invoiceEvent.Name == InvoiceEvent.ReceivedPayment &&
                invoiceEvent.Payment.GetPaymentMethodId()?.PaymentType == BitcoinPaymentType.Instance &&
                invoiceEvent.Payment.GetCryptoPaymentData() is BitcoinLikePaymentData bitcoinLikePaymentData)
            {
                var walletId = new WalletId(invoiceEvent.Invoice.StoreId, invoiceEvent.Payment.GetCryptoCode());
                var transactionId = bitcoinLikePaymentData.Outpoint.Hash;
                var labels = new List<Label>
                {
                    UpdateTransactionLabel.InvoiceLabelTemplate(invoiceEvent.Invoice.Id)
                };
                foreach (var paymentId in PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(UpdateTransactionLabel.PaymentRequestLabelTemplate(paymentId));
                }
                foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(UpdateTransactionLabel.AppLabelTemplate(appId));
                }
                _eventAggregator.Publish(new UpdateTransactionLabel(walletId, transactionId, labels));
            }
            else if (evt is UpdateTransactionLabel updateTransactionLabel)
            {
                await Task.WhenAll(updateTransactionLabel.TransactionLabels.Select(async pair =>
                {
                    var txId = pair.Key.ToString();
                    await _walletRepository.AddLabels(updateTransactionLabel.WalletId, pair.Value.ToArray(), Array.Empty<string>(),
                        new[] {txId});
                }));
            }else if (evt is NewOnChainTransactionEvent newOnChainTransactionEvent)
            {
                await _walletRepository.AssociateTransactionToScripts(newOnChainTransactionEvent.NewTransactionEvent
                    .TransactionData.Transaction);
            }
        }
    }

    public class UpdateTransactionLabel
    {
        public UpdateTransactionLabel()
        {

        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, Label label)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<Label>> {{txId, new List<Label>() { label }}};
            
        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, List<(string color, Label label)> colorLabels)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<Label>> {{txId, colorLabels.Select(tuple => tuple.label).ToList()}};
        }  public UpdateTransactionLabel(WalletId walletId, uint256 txId, List<Label> labels)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<Label>> {{txId, labels}};
        }
        public static Dictionary<string, string> DefaultLabelColors = new Dictionary<string, string>()
        {
            {"payjoin", "#51b13e"},
            {"invoice", "#cedc21"},
            {"payment-request", "#489D77"},
            {"app", "#5093B6"},
            {"pj-exposed", "#51b13e"},
            {"payout", "#3F88AF"},
            {"receive", "#3F88AF"}
        };
        
        public static Label PayjoinLabelTemplate()
        {
            return new Label.RawLabel("payjoin", DefaultLabelColors["payjoin"]);
        }

        public static Label InvoiceLabelTemplate(string invoice)
        {
            return new Label.ReferenceLabel("invoice", invoice, DefaultLabelColors["invoice"]);
        }
        public static  Label  PaymentRequestLabelTemplate(string paymentRequestId)
        {
            return new Label.ReferenceLabel("payment-request", paymentRequestId, DefaultLabelColors["payment-request"]);
        }
        public static  Label  AppLabelTemplate(string appId)
        {
            return new Label.ReferenceLabel("app", appId, DefaultLabelColors["app"]);
        }
        
        public static  Label  ReceiveWalletLabel()
        {
            return new Label.RawLabel("receive", DefaultLabelColors["receive"]);
        }

        public static Label PayjoinExposedLabelTemplate(string invoice)
        {
            return new Label.ReferenceLabel("pj-exposed", invoice, DefaultLabelColors["pj-exposed"]);
        }

        public static Label PayoutTemplate(Dictionary<string, List<string>> pullPaymentToPayouts, string walletId)
        {
            return new Label.PayoutLabel()
            {
                PullPaymentPayouts = pullPaymentToPayouts,
                WalletId = walletId
            };
        }
        public WalletId WalletId { get; set; }
        public Dictionary<uint256, List<Label>> TransactionLabels { get; set; }
        public override string ToString()
        {
            var result = new StringBuilder();
            foreach (var transactionLabel in TransactionLabels)
            {
                result.AppendLine(CultureInfo.InvariantCulture,
                    $"Adding {transactionLabel.Value.Count} labels to {transactionLabel.Key} in wallet {WalletId}");
            }

            return result.ToString();
        }
    }
}
