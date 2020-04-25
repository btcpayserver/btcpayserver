using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly WalletRepository _walletRepository;
        private Channel<Func<Task>> _labels = Channel.CreateUnbounded<Func<Task>>();


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
            while (await _labels.Reader.WaitToReadAsync(_Cts.Token))
            {
                if (_labels.Reader.TryRead(out var evt))
                {
                    try
                    {
                        await evt.Invoke();
                    }
                    catch when (_Cts.Token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogWarning(ex, $"Unhandled exception in {this.GetType().Name}");
                    }
                }
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
                var labels = new List<(string color, string label)>
                {
                    (invoiceLabelColor, InvoiceLabelTemplate(invoiceEvent.Invoice.Id))
                };

                if (invoiceEvent.Invoice.GetPayments(invoiceEvent.Payment.GetCryptoCode()).Any(entity =>
                    entity.GetCryptoPaymentData() is BitcoinLikePaymentData pData &&
                    pData.PayjoinInformation?.CoinjoinTransactionHash == transactionId))
                {
                    labels.Add((payjoinLabelColor, "payjoin"));
                }

                AddLabels(new UpdateTransactionLabel()
                {
                    WalletId = walletId,
                    TransactionLabels =
                        new Dictionary<uint256, List<(string color, string label)>>() {{transactionId, labels}}
                });
            }
            else if (evt is UpdateTransactionLabel updateTransactionLabel)
            {
                AddLabels(updateTransactionLabel);
            }

            return Task.CompletedTask;
        }

        private void AddLabels(UpdateTransactionLabel updateTransactionLabel)
        {
            _labels.Writer.TryWrite(async () =>
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
            });
        }

        public static string InvoiceLabelTemplate(string invoice)
        {
            return JObject.FromObject(new {label = "invoice", id = invoice}).ToString();
        }

        public static string PayjoinExposed(string invoice)
        {
            return JObject.FromObject(new {label = "pj-exposed", id = invoice}).ToString();
        }
    }

    public class UpdateTransactionLabel
    {
        public WalletId WalletId { get; set; }
        public Dictionary<uint256, List<(string color, string label)>> TransactionLabels { get; set; }
    }
}
