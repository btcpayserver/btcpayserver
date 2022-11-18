#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
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
        private readonly WalletRepository _walletRepository;

        public TransactionLabelMarkerHostedService(EventAggregator eventAggregator, WalletRepository walletRepository, Logs logs) :
            base(eventAggregator, logs)
        {
            _walletRepository = walletRepository;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<NewOnChainTransactionEvent>();
        }
        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case NewOnChainTransactionEvent transactionEvent:
                {
                    var txHash = transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();
                    
                    // find all wallet objects that fit this transaction
                    // that means see if there are any utxo objects that match in/outs and scripts/addresses that match outs
                    var matchedObjects = transactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs
                        .Select(txIn => new ObjectTypeId(WalletObjectData.Types.Utxo, txIn.PrevOut.ToString()))
                        .Concat(transactionEvent.NewTransactionEvent.TransactionData.Transaction.Outputs.AsIndexedOutputs().SelectMany(txOut =>
                           
                            new[]{
                            new ObjectTypeId(WalletObjectData.Types.Script,txOut.TxOut.ScriptPubKey.ToHex()),
                            new ObjectTypeId(WalletObjectData.Types.Utxo,txOut.ToCoin().Outpoint.ToString())
                            
                            } )).Distinct().ToArray();
                    
                    // we are intentionally excluding wallet id filter so that we reduce db trips
                    var objs = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery(){TypesIds = matchedObjects});

                    foreach (var walletObjectDatas in objs.GroupBy(data => data.Key.WalletId))
                    {
                        var txWalletObject = new WalletObjectId(walletObjectDatas.Key,
                            WalletObjectData.Types.Tx, txHash);
                        await _walletRepository.EnsureWalletObject(txWalletObject);
                        foreach (var walletObjectData in walletObjectDatas)
                        {
                            await _walletRepository.EnsureWalletObjectLink(txWalletObject, walletObjectData.Key);
                        }
                    }

                    break;
                }
                case InvoiceEvent {Name: InvoiceEvent.ReceivedPayment} invoiceEvent when
                    invoiceEvent.Payment.GetPaymentMethodId()?.PaymentType == BitcoinPaymentType.Instance &&
                    invoiceEvent.Payment.GetCryptoPaymentData() is BitcoinLikePaymentData bitcoinLikePaymentData:
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
                    break;
                }
            }
        }
    }
}
