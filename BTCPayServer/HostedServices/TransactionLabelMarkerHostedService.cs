#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly WalletRepository _walletRepository;

        public TransactionLabelMarkerHostedService(PaymentMethodHandlerDictionary handlers, EventAggregator eventAggregator, WalletRepository walletRepository, Logs logs) :
            base(eventAggregator, logs)
        {
            _handlers = handlers;
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
                // For each new transaction that we detect, we check if we can find
                // any utxo or script object matching it.
                // If we find, then we create a link between them and the tx object.
                case NewOnChainTransactionEvent transactionEvent:
                    {
                        var handler = _handlers.TryGetBitcoinHandler(transactionEvent.CryptoCode);
                        var derivation = transactionEvent.NewTransactionEvent.DerivationStrategy;
                        if (handler is null || derivation is null)
                            break;
                        var txHash = transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();

                        // find all wallet objects that fit this transaction
                        // that means see if there are any utxo objects that match in/outs and scripts/addresses that match outs

                        var matchedObjects = new List<ObjectTypeId>();
                        // Check if inputs match some UTXOs
                        foreach (var txIn in transactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs)
                        {
                            matchedObjects.Add(new ObjectTypeId(WalletObjectData.Types.Utxo, txIn.PrevOut.ToString()));
                        }

                        // Check if outputs match some UTXOs
                        var walletOutputsByIndex = transactionEvent.NewTransactionEvent.Outputs.ToDictionary(o => (uint)o.Index);
                        foreach (var txOut in transactionEvent.NewTransactionEvent.TransactionData.Transaction.Outputs.AsIndexedOutputs())
                        {
                            BitcoinAddress? address = null;
                            // Technically, walletTxOut.Address can be calculated.
                            // However in liquid for example, this returns the blinded address
                            // rather than the unblinded one.
                            if (walletOutputsByIndex.TryGetValue(txOut.N, out var walletTxOut))
                                address = walletTxOut.Address;
                            address ??= txOut.TxOut.ScriptPubKey.GetDestinationAddress(handler.Network.NBitcoinNetwork);
                            if (address is not null)
                                matchedObjects.Add(new ObjectTypeId(WalletObjectData.Types.Address, address.ToString()));
                            matchedObjects.Add(new ObjectTypeId(WalletObjectData.Types.Utxo, new OutPoint(transactionEvent.NewTransactionEvent.TransactionData.TransactionHash, txOut.N).ToString()));
                        }

                        var objs = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery() { TypesIds = matchedObjects.Distinct().ToArray() });
                        var links  = new List<WalletObjectLinkData>(); 
                        foreach (var walletObjectDatas in objs.GroupBy(data => data.Key.WalletId))
                        {
                            var txWalletObject = new WalletObjectId(walletObjectDatas.Key,
                                WalletObjectData.Types.Tx, txHash);
                            foreach (var walletObjectData in walletObjectDatas)
                            {
                                links.Add(
                                    WalletRepository.NewWalletObjectLinkData(txWalletObject, walletObjectData.Key));
                                //if the object is an address, we also link the labels to the tx
                                if (walletObjectData.Value.Type == WalletObjectData.Types.Address)
                                {
                                    var neighbours = walletObjectData.Value.GetNeighbours().ToArray();
                                    var labels = neighbours
                                        .Where(data => data.Type == WalletObjectData.Types.Label).Select(data =>
                                            new WalletObjectId(walletObjectDatas.Key, data.Type, data.Id));
                                    foreach (var label in labels)
                                    {
                                        links.Add(WalletRepository.NewWalletObjectLinkData(label, txWalletObject));
                                        var attachments = neighbours.Where(data => data.Type == label.Id);
                                        foreach (var attachment in attachments)
                                        {
                                            links.Add(WalletRepository.NewWalletObjectLinkData(new WalletObjectId(walletObjectDatas.Key, attachment.Type, attachment.Id), txWalletObject));
                                        }
                                    }
                                }
                            }
                        }
                        await _walletRepository.EnsureCreated(null,links);

                        break;
                    }
                case InvoiceEvent { Name: InvoiceEvent.ReceivedPayment } invoiceEvent when
                    _handlers.TryGetValue(invoiceEvent.Payment.PaymentMethodId, out var h) && h is BitcoinLikePaymentHandler handler:
                    {
                        var walletId = new WalletId(invoiceEvent.Invoice.StoreId, invoiceEvent.Payment.Currency);
                        var transactionId = handler.ParsePaymentDetails(invoiceEvent.Payment.Details).Outpoint.Hash;
                        var labels = new List<Attachment>
                    {
                        Attachment.Invoice(invoiceEvent.Invoice.Id)
                    };
                        labels.AddRange(PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice).Select(Attachment.PaymentRequest));
                        labels.AddRange(AppService.GetAppInternalTags(invoiceEvent.Invoice).Select(Attachment.App));

                        await _walletRepository.AddWalletTransactionAttachment(walletId, transactionId, labels);
                        break;
                    }
            }
        }
    }
}
