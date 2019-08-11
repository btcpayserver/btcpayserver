using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Monero.RPC;
using BTCPayServer.Monero.RPC.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroListener : IHostedService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly MoneroRPCProvider _moneroRpcProvider;
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<MoneroListener> _logger;
        private CompositeDisposable leases = new CompositeDisposable();


        private CancellationTokenSource _Cts;

        public MoneroListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            MoneroRPCProvider moneroRpcProvider,
            MoneroLikeConfiguration moneroLikeConfiguration, 
            BTCPayNetworkProvider networkProvider,
            ILogger<MoneroListener> logger)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _moneroRpcProvider = moneroRpcProvider;
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<MoneroEvent>(e => _ = OnMoneroEvent(e)));
            leases.Add(_eventAggregator.Subscribe<MoneroRPCProvider.MoneroDaemonStateChange>(e =>
            {
                if (_moneroRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ =UpdateAnyPendingMoneroLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));
            return Task.CompletedTask;
        }

        private async Task OnMoneroEvent(MoneroEvent obj)
        {

            var paymentMethodId = new PaymentMethodId(obj.CryptoCode, MoneroPaymentType.Instance);
            if (!string.IsNullOrEmpty(obj.BlockHash))
            {
                
                _logger.LogInformation($"{obj.CryptoCode}: New Block");
                OnNewBlock(obj.CryptoCode);
            }

            if (!string.IsNullOrEmpty(obj.TransactionHash))
            {
                
                _logger.LogInformation($"{obj.CryptoCode}: New Tx");
                var transfer = await _moneroRpcProvider.WalletRpcClients[obj.CryptoCode]
                    .GetTransferByTransactionId(
                        new GetTransferByTransactionIdRequest() {TransactionId = obj.TransactionHash});

                var invoicesToUpdate = new Dictionary<string, HashSet<string>>();

                foreach (var destination in transfer.Transfer.Destinations.GroupBy(destination => destination.Address))
                {
                    var address = destination.Key + "#" + paymentMethodId;
                    var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {address})).FirstOrDefault();
                    if (invoice == null)
                    {
                        continue;
                    }

                    
                    var addressIndex = await _moneroRpcProvider.WalletRpcClients[obj.CryptoCode]
                        .GetAddressIndex(new GetAddressIndexRequest() {Address = destination.Key});

                    var paymentData = new MoneroLikePaymentData()
                    {
                        Address = destination.Key,
                        SubaccountIndex = addressIndex.Index.Major,
                        SubaddressIndex = addressIndex.Index.Minor,
                        TransactionId = transfer.Transfer.Txid,
                        ConfirmationCount = transfer.Transfer.Confirmations,
                        Amount = destination.Sum(destination1 => destination1.Amount),
                        BlockHeight = transfer.Transfer.Height,

                    };

                    var alreadyExist = GetAllMoneroLikePayments(invoice, obj.CryptoCode)
                        .Select(entity => entity.GetCryptoPaymentData()).Any(c => c.GetPaymentId() == paymentData.GetPaymentId());
                    if (!alreadyExist)
                    {
                        var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                            paymentData, _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(obj.CryptoCode));
                        if (payment != null)
                            await ReceivedPayment(invoice, payment);
                    }
                    else
                    {
                        if (!invoicesToUpdate.ContainsKey(obj.CryptoCode))
                        {
                            invoicesToUpdate.Add(obj.CryptoCode, new HashSet<string>());
                        }

                        if (!invoicesToUpdate[obj.CryptoCode].Contains(invoice.Id))
                        {
                            invoicesToUpdate[obj.CryptoCode].Add(invoice.Id);
                        }
                    }
                }

                foreach (var cryptoCode in invoicesToUpdate)
                {
                    foreach (var invoiceId in cryptoCode.Value)
                    {
                        _ = UpdatePaymentStates(cryptoCode.Key, invoiceId);
                    }
                }
            }

        }

        private async Task<InvoiceEntity> ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            var paymentData = (MoneroLikePaymentData)payment.GetCryptoPaymentData();
            invoice = (await UpdatePaymentStates(payment.GetCryptoCode(), invoice.Id));
            if (invoice == null)
                return null;
            var paymentMethod = invoice.GetPaymentMethod(payment.Network, MoneroPaymentType.Instance);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is MoneroLikeOnChainPaymentMethodDetails monero &&
                monero.GetPaymentDestination() == paymentData.GetDestination() &&
                paymentMethod.Calculate().Due > Money.Zero)
            {
                var walletClient = _moneroRpcProvider.WalletRpcClients[payment.GetCryptoCode()];

                var address = await walletClient.CreateAddress(new CreateAddressRequest()
                {
                    Label = $"btcpay invoice #{invoice.Id}", AccountIndex = monero.AccountIndex
                });
                monero.DepositAddress = address.Address;
                monero.AddressIndex = address.AddressIndex;
                await _invoiceRepository.NewAddress(invoice.Id, monero, payment.Network);
                _eventAggregator.Publish(new InvoiceNewAddressEvent(invoice.Id, address.ToString(), payment.Network));
                paymentMethod.SetPaymentMethodDetails(monero);
                invoice.SetPaymentMethod(paymentMethod);
            }

            _eventAggregator.Publish(new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) {Payment = payment});
            return invoice;
        }


        private async Task<InvoiceEntity> UpdatePaymentStates(string cryptoCode, string invoiceId)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, false);
            if (invoice == null)
                return null;
            var updatedPaymentEntities = new List<PaymentEntity>();
            var moneroWalletRpcClient = _moneroRpcProvider.WalletRpcClients[cryptoCode];
            var payments = GetAllMoneroLikePayments(invoice, cryptoCode);

            //there are no monero like payments of the specified cryptoCode, move on
            if (!payments.Any())
            {
                return invoice;
            }

            var paymentData = payments.Select(entity => (Payment: entity, PaymentData: (MoneroLikePaymentData)entity.GetCryptoPaymentData() ));
            var paymentDataGroupedBySubAccount = paymentData.GroupBy(data => data.PaymentData.SubaccountIndex);

            var tasks = paymentDataGroupedBySubAccount.ToDictionary(datas => datas.Key,
                datas => moneroWalletRpcClient.GetTransfers(new GetTransfersRequest()
                {
                    AccountIndex = datas.Key,
                    In = true,
                    SubaddrIndices = datas.Select(data => data.PaymentData.SubaddressIndex).ToList()
                }));

            await Task.WhenAll(tasks.Values);

            foreach (var keyValuePair in tasks)
            {
                var accountIndex = keyValuePair.Key;
                var transfers = keyValuePair.Value.Result.In;

                var newTxs = paymentDataGroupedBySubAccount.Where(tuples =>
                    tuples.Key == accountIndex && !tuples.Any(tuple =>
                        transfers.Any(item => tuple.PaymentData.TransactionId == item.Txid)));
                
                foreach (var transfer in transfers)
                {
                        transfer.SubaddrIndex
                }
            }


//            
//            var conflicts = GetConflicts(transactions.Select(t => t.Value));
//            foreach (var payment in invoice.GetPayments(wallet.Network))
//            {
//                if (payment.GetPaymentMethodId().PaymentType != PaymentTypes.BTCLike)
//                    continue;
//                var paymentData = (BitcoinLikePaymentData)payment.GetCryptoPaymentData();
//                if (!transactions.TryGetValue(paymentData.Outpoint.Hash, out TransactionResult tx))
//                    continue;
//                var txId = tx.Transaction.GetHash();
//                var txConflict = conflicts.GetConflict(txId);
//                var accounted = txConflict == null || txConflict.IsWinner(txId);
//
//                bool updated = false;
//                if (accounted != payment.Accounted)
//                {
//                    updated = true;
//                    payment.Accounted = accounted;
//                }
//
//                if (paymentData.ConfirmationCount != tx.Confirmations)
//                {
//                    if (wallet.Network.MaxTrackedConfirmation >= paymentData.ConfirmationCount)
//                    {
//                        paymentData.ConfirmationCount = tx.Confirmations;
//                        payment.SetCryptoPaymentData(paymentData);
//                        updated = true;
//                    }
//                }
//
//                // if needed add invoice back to pending to track number of confirmations
//                if (paymentData.ConfirmationCount < wallet.Network.MaxTrackedConfirmation)
//                    await _InvoiceRepository.AddPendingInvoiceIfNotPresent(invoice.Id);
//
//                if (updated)
//                    updatedPaymentEntities.Add(payment);
//            }
            await _invoiceRepository.UpdatePayments(updatedPaymentEntities);
            if (updatedPaymentEntities.Count != 0)
                _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
            return _eventAggregator;
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            return Task.CompletedTask;
        }
        
        private void OnNewBlock(string cryptoCode)
        {
            _ =UpdateAnyPendingMoneroLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent() {CryptoCode = cryptoCode});
        }

        private async Task UpdateAnyPendingMoneroLikePayment(string cryptoCode)
        {
            await Task.WhenAll((await _invoiceRepository.GetPendingInvoices())
                .Select(invoiceId => UpdatePaymentStates(cryptoCode, invoiceId))
                .ToArray());
        }

        private  IEnumerable<PaymentEntity> GetAllMoneroLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments()
                .Where(p => p.GetPaymentMethodId() == new PaymentMethodId(cryptoCode, MoneroPaymentType.Instance));
        }
    }
}
