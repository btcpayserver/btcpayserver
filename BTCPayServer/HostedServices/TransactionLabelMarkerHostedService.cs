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
                var labels = new List<(string color, Label label)>
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
                var walletTransactionsInfo =
                    await _walletRepository.GetWalletTransactionsInfo(updateTransactionLabel.WalletId);
                var walletBlobInfo = await _walletRepository.GetWalletInfo(updateTransactionLabel.WalletId);
                await Task.WhenAll(updateTransactionLabel.TransactionLabels.Select(async pair =>
                {
                    var txId = pair.Key.ToString();
                    var coloredLabels = pair.Value;
                    if (!walletTransactionsInfo.TryGetValue(txId, out var walletTransactionInfo))
                    {
                        walletTransactionInfo = new WalletTransactionInfo();
                    }

                    bool walletNeedUpdate = false;
                    foreach (var cl in coloredLabels)
                    {
                        if (walletBlobInfo.LabelColors.TryGetValue(cl.label.Text, out var currentColor))
                        {
                            if (currentColor != cl.color)
                            {
                                walletNeedUpdate = true;
                                walletBlobInfo.LabelColors[cl.label.Text] = currentColor;
                            }
                        }
                        else
                        {
                            walletNeedUpdate = true;
                            walletBlobInfo.LabelColors.AddOrReplace(cl.label.Text, cl.color);
                        }
                    }

                    if (walletNeedUpdate)
                        await _walletRepository.SetWalletInfo(updateTransactionLabel.WalletId, walletBlobInfo);
                    foreach (var cl in coloredLabels)
                    {
                        var label = cl.label;
                        if (walletTransactionInfo.Labels.TryGetValue(label.Text, out var existingLabel))
                        {
                            label = label.Merge(existingLabel);
                        }

                        walletTransactionInfo.Labels.AddOrReplace(label.Text, label);
                    }

                    await _walletRepository.SetWalletTransactionInfo(updateTransactionLabel.WalletId,
                        txId, walletTransactionInfo);
                }));
            }
        }
    }

    public class UpdateTransactionLabel
    {
        public UpdateTransactionLabel()
        {

        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, (string color, Label label) colorLabel)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<(string color, Label label)>>();
            TransactionLabels.Add(txId, new List<(string color, Label label)>() { colorLabel });
        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, List<(string color, Label label)> colorLabels)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<(string color, Label label)>>();
            TransactionLabels.Add(txId, colorLabels);
        }
        public static (string color, Label label) PayjoinLabelTemplate()
        {
            return ("#51b13e", new RawLabel("payjoin"));
        }

        public static (string color, Label label) InvoiceLabelTemplate(string invoice)
        {
            return ("#cedc21", new ReferenceLabel("invoice", invoice));
        }
        public static (string color, Label label) PaymentRequestLabelTemplate(string paymentRequestId)
        {
            return ("#489D77", new ReferenceLabel("payment-request", paymentRequestId));
        }
        public static (string color, Label label) AppLabelTemplate(string appId)
        {
            return ("#5093B6", new ReferenceLabel("app", appId));
        }

        public static (string color, Label label) PayjoinExposedLabelTemplate(string invoice)
        {
            return ("#51b13e", new ReferenceLabel("pj-exposed", invoice));
        }

        public static (string color, Label label) PayoutTemplate(Dictionary<string, List<string>> pullPaymentToPayouts, string walletId)
        {
            return ("#3F88AF", new PayoutLabel()
            {
                PullPaymentPayouts = pullPaymentToPayouts,
                WalletId = walletId
            });
        }
        public WalletId WalletId { get; set; }
        public Dictionary<uint256, List<(string color, Label label)>> TransactionLabels { get; set; }
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
