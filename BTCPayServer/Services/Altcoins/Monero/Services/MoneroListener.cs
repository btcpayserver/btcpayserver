using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.RPC;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class MoneroListener : IHostedService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly MoneroRPCProvider _moneroRpcProvider;
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
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
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.Any())
            {
                return Task.CompletedTask;
            }
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<MoneroEvent>(OnMoneroEvent));
            leases.Add(_eventAggregator.Subscribe<MoneroRPCProvider.MoneroDaemonStateChange>(e =>
            {
                if (_moneroRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ = UpdateAnyPendingMoneroLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));

            return Task.CompletedTask;
        }

        private void OnMoneroEvent(MoneroEvent obj)
        {
            if (!_moneroRpcProvider.IsAvailable(obj.CryptoCode))
            {
                return;
            }

            if (!string.IsNullOrEmpty(obj.BlockHash))
            {
                OnNewBlock(obj.CryptoCode);
            }

            if (!string.IsNullOrEmpty(obj.TransactionHash))
            {
                _ = OnTransactionUpdated(obj.CryptoCode, obj.TransactionHash);
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.GetCryptoPaymentData().GetValue()} {payment.GetCryptoCode()} {payment.GetCryptoPaymentData().GetPaymentId()}");
            var paymentData = (MoneroLikePaymentData)payment.GetCryptoPaymentData();
            var paymentMethod = invoice.GetPaymentMethod(payment.Network, MoneroPaymentType.Instance);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is MoneroLikeOnChainPaymentMethodDetails monero &&
                monero.GetPaymentDestination() == paymentData.GetDestination() &&
                paymentMethod.Calculate().Due > Money.Zero)
            {
                var walletClient = _moneroRpcProvider.WalletRpcClients[payment.GetCryptoCode()];

                var address = await walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>(
                    "create_address",
                    new CreateAddressRequest()
                    {
                        Label = $"btcpay invoice #{invoice.Id}", AccountIndex = monero.AccountIndex
                    });
                monero.DepositAddress = address.Address;
                monero.AddressIndex = address.AddressIndex;
                await _invoiceRepository.NewAddress(invoice.Id, monero, payment.Network);
                _eventAggregator.Publish(
                    new InvoiceNewAddressEvent(invoice.Id, address.Address, payment.Network));
                paymentMethod.SetPaymentMethodDetails(monero);
                invoice.SetPaymentMethod(paymentMethod);
            }

            _eventAggregator.Publish(
                new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) {Payment = payment});
        }

        private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices)
        {
            if (!invoices.Any())
            {
                return;
            }

            var moneroWalletRpcClient = _moneroRpcProvider.WalletRpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);


            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllMoneroLikePayments(entity, cryptoCode),
                    PaymentMethodDetails: entity.GetPaymentMethod(network, MoneroPaymentType.Instance)
                        .GetPaymentMethodDetails() as MoneroLikeOnChainPaymentMethodDetails))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: (MoneroLikePaymentData)entity.GetCryptoPaymentData(),
                            tuple.Invoice))
                ));

            var existingPaymentData = expandedInvoices.SelectMany(tuple => tuple.ExistingPayments);

            var accountToAddressQuery = new Dictionary<long, List<long>>();
            //create list of subaddresses to account to query the monero wallet
            foreach (var expandedInvoice in expandedInvoices)
            {
                var addressIndexList =
                    accountToAddressQuery.GetValueOrDefault(expandedInvoice.PaymentMethodDetails.AccountIndex,
                        new List<long>());

                addressIndexList.AddRange(
                    expandedInvoice.ExistingPayments.Select(tuple => tuple.PaymentData.SubaddressIndex));
                addressIndexList.Add(expandedInvoice.PaymentMethodDetails.AddressIndex);
                accountToAddressQuery.AddOrReplace(expandedInvoice.PaymentMethodDetails.AccountIndex, addressIndexList);
            }

            var tasks = accountToAddressQuery.ToDictionary(datas => datas.Key,
                datas => moneroWalletRpcClient.SendCommandAsync<GetTransfersRequest, GetTransfersResponse>(
                    "get_transfers",
                    new GetTransfersRequest()
                    {
                        AccountIndex = datas.Key, In = true, SubaddrIndices = datas.Value.Distinct().ToList()
                    }));

            await Task.WhenAll(tasks.Values);


            var transferProcessingTasks = new List<Task>();

            var updatedPaymentEntities = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
            foreach (var keyValuePair in tasks)
            {
                var transfers = keyValuePair.Value.Result.In;
                if (transfers == null)
                {
                    continue;
                }

                transferProcessingTasks.AddRange(transfers.Select(transfer =>
                {
                    InvoiceEntity invoice = null;
                    var existingMatch = existingPaymentData.SingleOrDefault(tuple =>
                        tuple.PaymentData.Address == transfer.Address &&
                        tuple.PaymentData.TransactionId == transfer.Txid);

                    if (existingMatch.Invoice != null)
                    {
                        invoice = existingMatch.Invoice;
                    }
                    else
                    {
                        var newMatch = expandedInvoices.SingleOrDefault(tuple =>
                            tuple.PaymentMethodDetails.GetPaymentDestination() == transfer.Address);

                        if (newMatch.Invoice == null)
                        {
                            return Task.CompletedTask;
                        }

                        invoice = newMatch.Invoice;
                    }


                    return HandlePaymnetData(cryptoCode, transfer.Address, transfer.Amount, transfer.SubaddrIndex.Major,
                        transfer.SubaddrIndex.Minor, transfer.Txid, transfer.Confirmations, transfer.Height, invoice,
                        updatedPaymentEntities);
                }));
            }

            transferProcessingTasks.Add(
                _invoiceRepository.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList()));
            await Task.WhenAll(transferProcessingTasks);
            foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.Item2))
            {
                if (valueTuples.Any())
                {
                    _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                }
            }
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts?.Cancel();
            return Task.CompletedTask;
        }

        private void OnNewBlock(string cryptoCode)
        {
            _ = UpdateAnyPendingMoneroLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent() {CryptoCode = cryptoCode});
        }

        private async Task OnTransactionUpdated(string cryptoCode, string transactionHash)
        {
            var paymentMethodId = new PaymentMethodId(cryptoCode, MoneroPaymentType.Instance);
            var transfer = await _moneroRpcProvider.WalletRpcClients[cryptoCode]
                .SendCommandAsync<GetTransferByTransactionIdRequest, GetTransferByTransactionIdResponse>(
                    "get_transfer_by_txid",
                    new GetTransferByTransactionIdRequest() {TransactionId = transactionHash});

            var paymentsToUpdate = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();


            //group all destinations of the tx together and loop through the sets
            foreach (var destination in transfer.Transfers.GroupBy(destination => destination.Address))
            {
                //find the invoice corresponding to this address, else skip
                var address = destination.Key + "#" + paymentMethodId;
                var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {address})).FirstOrDefault();
                if (invoice == null)
                {
                    continue;
                }

                var index = destination.First().SubaddrIndex;

                await HandlePaymnetData(cryptoCode,
                    destination.Key,
                    destination.Sum(destination1 => destination1.Amount),
                    index.Major,
                    index.Minor,
                    transfer.Transfer.Txid,
                    transfer.Transfer.Confirmations,
                    transfer.Transfer.Height
                    , invoice, paymentsToUpdate);
            }

            if (paymentsToUpdate.Any())
            {
                await _invoiceRepository.UpdatePayments(paymentsToUpdate.Select(tuple => tuple.Payment).ToList());
                foreach (var valueTuples in paymentsToUpdate.GroupBy(entity => entity.invoice))
                {
                    if (valueTuples.Any())
                    {
                        _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                    }
                }
            }
        }

        private async Task HandlePaymnetData(string cryptoCode, string address, long totalAmount, long subaccountIndex,
            long subaddressIndex,
            string txId, long confirmations, long blockHeight, InvoiceEntity invoice,
            BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            //construct the payment data
            var paymentData = new MoneroLikePaymentData()
            {
                Address = address,
                SubaccountIndex = subaccountIndex,
                SubaddressIndex = subaddressIndex,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                Amount = totalAmount,
                BlockHeight = blockHeight,
                Network = _networkProvider.GetNetwork(cryptoCode)
            };

            //check if this tx exists as a payment to this invoice already
            var alreadyExistingPaymentThatMatches = GetAllMoneroLikePayments(invoice, cryptoCode)
                .Select(entity => (Payment: entity, PaymentData: entity.GetCryptoPaymentData()))
                .SingleOrDefault(c => c.PaymentData.GetPaymentId() == paymentData.GetPaymentId());

            //if it doesnt, add it and assign a new monerolike address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches.Payment == null)
            {
                var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                    paymentData, _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(cryptoCode), true);
                if (payment != null)
                    await ReceivedPayment(invoice, payment);
            }
            else
            {
                //else update it with the new data
                alreadyExistingPaymentThatMatches.PaymentData = paymentData;
                alreadyExistingPaymentThatMatches.Payment.SetCryptoPaymentData(paymentData);
                paymentsToUpdate.Add((alreadyExistingPaymentThatMatches.Payment, invoice));
            }
        }

        private async Task UpdateAnyPendingMoneroLikePayment(string cryptoCode)
        {
            var invoiceIds =
                await GetPendingInvoicesWithPaymentMethodOption(new PaymentMethodId(cryptoCode,
                    MoneroPaymentType.Instance));
            if (!invoiceIds.Any())
            {
                return;
            }

            var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery() {InvoiceId = invoiceIds});
            _logger.LogInformation($"Updating pending payments for {cryptoCode} in {string.Join(',', invoiceIds)}");
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private async Task<string[]> GetPendingInvoicesWithPaymentMethodOption(PaymentMethodId paymentMethodId)
        {
            return await _invoiceRepository.GetPendingInvoices(pendingInvoice =>
                pendingInvoice.Where(data => data.InvoiceData.AddressInvoices.Any(invoiceData =>
                    invoiceData.GetpaymentMethodId() != null && invoiceData.GetpaymentMethodId() == paymentMethodId)));
        }

        private IEnumerable<PaymentEntity> GetAllMoneroLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments()
                .Where(p => p.GetPaymentMethodId() == new PaymentMethodId(cryptoCode, MoneroPaymentType.Instance));
        }
    }
}
