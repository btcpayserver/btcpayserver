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
#nullable enable
    public record LabelTemplate(string DefaultColor, string Label, string? Id = null, JObject? AssociatedData = null)
    {
        public static LabelTemplate PayjoinLabelTemplate()
        {
            return new LabelTemplate("#51b13e", "payjoin");
        }

        public static LabelTemplate InvoiceLabelTemplate(string invoice)
        {
            return new LabelTemplate("#cedc21", "invoice", invoice);
        }
        public static LabelTemplate PaymentRequestLabelTemplate(string paymentRequestId)
        {
            return new LabelTemplate("#489D77", "payment-request", paymentRequestId);
        }
        public static LabelTemplate AppLabelTemplate(string appId)
        {
            return new LabelTemplate("#5093B6", "app", appId);
        }

        public static LabelTemplate PayjoinExposedLabelTemplate(string invoice)
        {
            return new LabelTemplate("#51b13e", "pj-exposed", invoice);
        }

        public static LabelTemplate PayoutTemplate(string pullPaymentId, string payoutId)
        {
            return new LabelTemplate("#3F88AF", "payout", payoutId, string.IsNullOrEmpty(pullPaymentId) ? null : new JObject()
            {
                ["pullPaymentId"] = pullPaymentId
            });
        }
    }

#nullable restore
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
                var labels = new List<LabelTemplate>
                {
                    LabelTemplate.InvoiceLabelTemplate(invoiceEvent.Invoice.Id)
                };
                foreach (var paymentId in PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(LabelTemplate.PaymentRequestLabelTemplate(paymentId));
                }
                foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice))
                {
                    labels.Add(LabelTemplate.AppLabelTemplate(appId));
                }



                _eventAggregator.Publish(new UpdateTransactionLabel(walletId, transactionId, labels));
            }
            else if (evt is UpdateTransactionLabel updateTransactionLabel)
            {
                await Task.WhenAll(updateTransactionLabel.TransactionLabels.Select(async pair =>
                {
                    var txObjId = new WalletObjectId(updateTransactionLabel.WalletId, WalletObjectData.Types.Transaction, pair.Key.ToString());
                    await _walletRepository.EnsureWalletObject(txObjId);
                    foreach (var labelTemplate in pair.Value)
                    {
                        var labelObjId = new WalletObjectId(updateTransactionLabel.WalletId, WalletObjectData.Types.Label, labelTemplate.Label);
                        await _walletRepository.EnsureWalletObject(labelObjId, new JObject()
                        {
                            ["color"] = labelTemplate.DefaultColor
                        });
                        await _walletRepository.EnsureWalletObjectLink(labelObjId, txObjId);
                        if (labelTemplate.AssociatedData is not null || labelTemplate.Id is not null)
                        {
                            var data = new WalletObjectId(updateTransactionLabel.WalletId, labelTemplate.Label, labelTemplate.Id ?? string.Empty);
                            await _walletRepository.EnsureWalletObject(data, labelTemplate.AssociatedData);
                            await _walletRepository.EnsureWalletObjectLink(data, txObjId);
                        }
                    }
                }));
            }
        }
    }

    public class UpdateTransactionLabel
    {
        public UpdateTransactionLabel(WalletId walletId, Dictionary<uint256, List<LabelTemplate>> transactionLabels)
        {
            WalletId = walletId;
            TransactionLabels = transactionLabels;
        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, LabelTemplate labelTemplate)
        {
            WalletId = walletId;
            TransactionLabels.Add(txId, new List<LabelTemplate>() { labelTemplate });
        }
        public UpdateTransactionLabel(WalletId walletId, uint256 txId, List<LabelTemplate> labelTemplates)
        {
            WalletId = walletId;
            TransactionLabels.Add(txId, labelTemplates);
        }
        public WalletId WalletId { get; set; }
        public Dictionary<uint256, List<LabelTemplate>> TransactionLabels { get; set; } = new Dictionary<uint256, List<LabelTemplate>>();
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
