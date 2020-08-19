using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly EventAggregator _eventAggregator;
        private readonly WalletRepository _walletRepository;

        public TransactionLabelMarkerHostedService(EventAggregator eventAggregator, WalletRepository walletRepository) :
            base(eventAggregator)
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
                var labels = new List<(string color, string label)>
                {
                    UpdateTransactionLabel.InvoiceLabelTemplate(invoiceEvent.Invoice.Id)
                };

                if (invoiceEvent.Invoice.GetPayments(invoiceEvent.Payment.GetCryptoCode()).Any(entity =>
                    entity.GetCryptoPaymentData() is BitcoinLikePaymentData pData &&
                    pData.PayjoinInformation?.CoinjoinTransactionHash == transactionId))
                {
                    labels.Add(UpdateTransactionLabel.PayjoinLabelTemplate());
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
                    if (!walletTransactionsInfo.TryGetValue(pair.Key.ToString(), out var walletTransactionInfo))
                    {
                        walletTransactionInfo = new WalletTransactionInfo();
                    }

                    foreach (var label in pair.Value)
                    {
                        walletBlobInfo.LabelColors.TryAdd(label.label, label.color);
                    }

                    await _walletRepository.SetWalletInfo(updateTransactionLabel.WalletId, walletBlobInfo);
                    var update = false;
                    foreach (var label in pair.Value)
                    {
                        if (walletTransactionInfo.Labels.Add(label.label))
                        {
                            update = true;
                        }
                    }

                    if (update)
                    {
                        await _walletRepository.SetWalletTransactionInfo(updateTransactionLabel.WalletId,
                            pair.Key.ToString(), walletTransactionInfo);
                    }
                }));
            }
        }
    }

    public class UpdateTransactionLabel
    {
        public UpdateTransactionLabel()
        {

        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, (string color, string label) colorLabel)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<(string color, string label)>>();
            TransactionLabels.Add(txId, new List<(string color, string label)>() { colorLabel });
        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, List<(string color, string label)> colorLabels)
        {
            WalletId = walletId;
            TransactionLabels = new Dictionary<uint256, List<(string color, string label)>>();
            TransactionLabels.Add(txId, colorLabels);
        }
        public static (string color, string label) PayjoinLabelTemplate()
        {
            return ("#51b13e", "payjoin");
        }

        public static (string color, string label) InvoiceLabelTemplate(string invoice)
        {
            return ("#cedc21", JObject.FromObject(new { value = "invoice", id = invoice }).ToString());
        }

        public static (string color, string label) PayjoinExposedLabelTemplate(string invoice)
        {
            return ("#51b13e", JObject.FromObject(new { value = "pj-exposed", id = invoice }).ToString());
        }

        public WalletId WalletId { get; set; }
        public Dictionary<uint256, List<(string color, string label)>> TransactionLabels { get; set; }
        public override string ToString()
        {
            var result = new StringBuilder();
            foreach (var transactionLabel in TransactionLabels)
            {
                result.AppendLine(
                    $"Adding {transactionLabel.Value.Count} labels to {transactionLabel.Key} in wallet {WalletId}");
            }

            return result.ToString();
        }
    }
}
