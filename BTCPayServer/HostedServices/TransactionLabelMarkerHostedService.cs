#nullable enable
using System;
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
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly WalletRepository _walletRepository;
        private readonly PaymentRequestRepository _paymentRequestRepository;

        public TransactionLabelMarkerHostedService(PaymentMethodHandlerDictionary handlers, EventAggregator eventAggregator, WalletRepository walletRepository, PaymentRequestRepository paymentRequestRepository, Logs logs) :
            base(eventAggregator, logs)
        {
            _handlers = handlers;
            _walletRepository = walletRepository;
            _paymentRequestRepository = paymentRequestRepository;
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
                        var handler = _handlers.TryGetBitcoinHandler(transactionEvent.PaymentMethodId);
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

                        matchedObjects.Add(new ObjectTypeId(WalletObjectData.Types.Tx, transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString()));
                        var objs = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery() { TypesIds = matchedObjects.Distinct().ToArray() });
                        var links = new List<WalletObjectLinkData>();
                        var newObjs = new List<WalletObjectData>();
                        foreach (var walletObjectDatas in objs.GroupBy(data => data.Key.WalletId))
                        {
                            var wid = walletObjectDatas.Key;
                            var txWalletObject = new WalletObjectId(wid, WalletObjectData.Types.Tx, txHash);

                            // if we are replacing a transaction, we add the same links to the new transaction
                            // Note that unlike CPFP, RBF tag may be applied to transactions that may not be fee bumps
                            {
                                if (transactionEvent.NewTransactionEvent.Replacing is not null)
                                {
                                    foreach (var replaced in transactionEvent.NewTransactionEvent.Replacing)
                                    {
                                        var replacedwoId = new WalletObjectId(wid,
                                              WalletObjectData.Types.Tx, replaced.ToString());
                                        var replacedo = await _walletRepository.GetWalletObject(replacedwoId);
                                        var replacedLinks = replacedo?.GetLinks().Where(t => t.type != WalletObjectData.Types.Tx) ?? [];
                                        var rbf = new WalletObjectId(wid, WalletObjectData.Types.RBF, "");
                                        var label = WalletRepository.CreateLabel(rbf);
                                        newObjs.Add(label.ObjectData);
                                        links.Add(WalletRepository.NewWalletObjectLinkData(txWalletObject, label.Id));
                                        links.Add(WalletRepository.NewWalletObjectLinkData(txWalletObject, rbf, new JObject()
                                        {
                                            ["txs"] = JArray.FromObject(new[] { replaced.ToString() })
                                        }));
                                        foreach (var link in replacedLinks)
                                        {
                                            links.Add(WalletRepository.NewWalletObjectLinkData(new WalletObjectId(walletObjectDatas.Key, link.type, link.id), txWalletObject, link.linkdata));
                                        }
                                    }
                                }
                            }

                            foreach (var walletObjectData in walletObjectDatas)
                            {
                                // if we detect it's a CPFP, we add a CPFP label
                                {
                                    // Only for non confirmed transaction where all inputs and outputs belong to the wallet and issued by us
                                    if (
                                        walletObjectData.Value.Type is WalletObjectData.Types.Tx &&
                                        walletObjectData.Value.GetData()?["bumpFeeMethod"] is JValue { Value: "CPFP" } &&
                                        transactionEvent.NewTransactionEvent is { BlockId: null, TransactionData: { Transaction: { } tx } } txEvt &&
                                        txEvt.Inputs.Count == tx.Inputs.Count &&
                                        txEvt.Outputs.Count == tx.Outputs.Count)
                                    {
                                        var cpfp = new WalletObjectId(wid, WalletObjectData.Types.CPFP, "");
                                        var label = WalletRepository.CreateLabel(cpfp);
                                        newObjs.Add(label.ObjectData);
                                        links.Add(WalletRepository.NewWalletObjectLinkData(txWalletObject, label.Id));
                                        links.Add(WalletRepository.NewWalletObjectLinkData(txWalletObject, cpfp, new JObject()
                                        {
                                            ["outpoints"] = JArray.FromObject(txEvt.Inputs.Select(i => $"{i.TransactionId}-{i.Index}"))
                                        }));
                                    }
                                }


                                // if we the tx is matching some known address and utxo, we link them to this tx
                                {
                                    if (walletObjectData.Value.Type is WalletObjectData.Types.Utxo or WalletObjectData.Types.Address)
                                        links.Add(
                                            WalletRepository.NewWalletObjectLinkData(txWalletObject, walletObjectData.Key));
                                }
                                // if the object is an address, we also link its labels (the ones added in the wallet receive page)
                                {
                                    if (walletObjectData.Value.Type == WalletObjectData.Types.Address)
                                    {
                                        var neighbours = walletObjectData.Value.GetNeighbours().ToArray();
                                        var labels = neighbours
                                            .Where(data => data.Type == WalletObjectData.Types.Label).Select(data =>
                                                new WalletObjectId(wid, data.Type, data.Id));
                                        foreach (var label in labels)
                                        {
                                            links.Add(WalletRepository.NewWalletObjectLinkData(label, txWalletObject));
                                            var attachments = neighbours.Where(data => data.Type == label.Id);
                                            foreach (var attachment in attachments)
                                            {
                                                links.Add(WalletRepository.NewWalletObjectLinkData(new WalletObjectId(wid, attachment.Type, attachment.Id), txWalletObject));
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        await _walletRepository.EnsureCreated(newObjs, links);

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

                        var paymentRequestIds = PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice).ToArray();
                        if (paymentRequestIds.Length > 0)
                        {
                            var paymentRequests = await _paymentRequestRepository.FindPaymentRequests(
                                new PaymentRequestQuery { Ids = paymentRequestIds });
                            var paymentRequestsById = paymentRequests.ToDictionary(pr => pr.Id);

                            foreach (var prId in paymentRequestIds)
                            {
                                var data = paymentRequestsById.TryGetValue(prId, out var pr) && pr.Title is { } title
                                    ? new JObject { ["title"] = title }
                                    : null;
                                labels.Add(Attachment.PaymentRequest(prId, data));
                            }
                        }

                        labels.AddRange(AppService.GetAppInternalTags(invoiceEvent.Invoice).Select(Attachment.App));

                        await _walletRepository.AddWalletTransactionAttachment(walletId, transactionId, labels);
                        break;
                    }
            }
        }
    }
}
