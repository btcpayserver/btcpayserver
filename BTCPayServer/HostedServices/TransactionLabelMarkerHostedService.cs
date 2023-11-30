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
using BTCPayServer.Services.PaymentRequests;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.HostedServices
{
    public class TransactionLabelMarkerHostedService : EventHostedServiceBase
    {
        private readonly WalletRepository _walletRepository;

        public BTCPayNetworkProvider NetworkProvider { get; }

        public TransactionLabelMarkerHostedService(BTCPayNetworkProvider networkProvider, EventAggregator eventAggregator, WalletRepository walletRepository, Logs logs) :
            base(eventAggregator, logs)
        {
            NetworkProvider = networkProvider;
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
                        var network = NetworkProvider.GetNetwork<BTCPayNetwork>(transactionEvent.CryptoCode);
                        var derivation = transactionEvent.NewTransactionEvent.DerivationStrategy;
                        if (network is null || derivation is null)
                            break;
                        var txHash = transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();

                        // find all wallet objects that fit this transaction
                        // that means see if there are any utxo objects that match in/outs and scripts/addresses that match outs
                        var matchedObjects = transactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs
                            .Select<TxIn, ObjectTypeId>(txIn => new ObjectTypeId(WalletObjectData.Types.Utxo, txIn.PrevOut.ToString()))
                            .Concat(transactionEvent.NewTransactionEvent.Outputs.SelectMany<NBXplorer.Models.MatchedOutput, ObjectTypeId>(txOut =>

                                new[]{
                            new ObjectTypeId(WalletObjectData.Types.Address, GetAddress(derivation, txOut, network).ToString()),
                            new ObjectTypeId(WalletObjectData.Types.Utxo, new OutPoint(transactionEvent.NewTransactionEvent.TransactionData.TransactionHash, (uint)txOut.Index).ToString())

                                })).Distinct().ToArray();

                        var objs = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery() { TypesIds = matchedObjects });
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
                    invoiceEvent.Payment.GetPaymentMethodId()?.PaymentType == BitcoinPaymentType.Instance &&
                    invoiceEvent.Payment.GetCryptoPaymentData() is BitcoinLikePaymentData bitcoinLikePaymentData:
                    {
                        var walletId = new WalletId(invoiceEvent.Invoice.StoreId, invoiceEvent.Payment.Currency);
                        var transactionId = bitcoinLikePaymentData.Outpoint.Hash;
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

        private BitcoinAddress GetAddress(DerivationStrategyBase derivationStrategy, NBXplorer.Models.MatchedOutput txOut, BTCPayNetwork network)
        {
            // Old version of NBX doesn't give address in the event, so we need to guess
            return (txOut.Address ?? network.NBXplorerNetwork.CreateAddress(derivationStrategy, txOut.KeyPath, txOut.ScriptPubKey));
        }
    }
}
