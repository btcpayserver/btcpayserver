#if ALTCOINS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
using BTCPayServer.Services.Altcoins.Zcash.RPC;
using BTCPayServer.Services.Altcoins.Zcash.RPC.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Altcoins.Zcash.Services
{
    public class ZcashListener : IHostedService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly ZcashRPCProvider _ZcashRpcProvider;
        private readonly ZcashLikeConfiguration _ZcashLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<ZcashListener> _logger;
        private readonly PaymentService _paymentService;
        private readonly CompositeDisposable leases = new CompositeDisposable();
        private readonly Channel<Func<Task>> _requests = Channel.CreateUnbounded<Func<Task>>();
        private CancellationTokenSource _Cts;

        public ZcashListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ZcashRPCProvider ZcashRpcProvider,
            ZcashLikeConfiguration ZcashLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<ZcashListener> logger, 
            PaymentService paymentService)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _ZcashRpcProvider = ZcashRpcProvider;
            _ZcashLikeConfiguration = ZcashLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _paymentService = paymentService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_ZcashLikeConfiguration.ZcashLikeConfigurationItems.Any())
            {
                return Task.CompletedTask;
            }
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<ZcashEvent>(OnZcashEvent));
            leases.Add(_eventAggregator.Subscribe<ZcashRPCProvider.ZcashDaemonStateChange>(e =>
            {
                if (_ZcashRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ = UpdateAnyPendingZcashLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));
            _ = WorkThroughQueue(_Cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkThroughQueue(CancellationToken token)
        {
            while (await _requests.Reader.WaitToReadAsync(token) && _requests.Reader.TryRead(out var action)) {
                token.ThrowIfCancellationRequested();
                try {
                    await action.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError($"error with action item {e}");
                }
            }
        }

        private void OnZcashEvent(ZcashEvent obj)
        {
            if (!_ZcashRpcProvider.IsAvailable(obj.CryptoCode))
            {
                return;
            }

            if (!string.IsNullOrEmpty(obj.BlockHash))
            {
                if (!_requests.Writer.TryWrite(() => OnNewBlock(obj.CryptoCode))) {
                    _logger.LogWarning($"Failed to write new block task to channel");
                }
            }

            if (!string.IsNullOrEmpty(obj.TransactionHash))
            {
                if (!_requests.Writer.TryWrite(() => OnTransactionUpdated(obj.CryptoCode, obj.TransactionHash))) {
                    _logger.LogWarning($"Failed to write new tx task to channel");
                }
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.GetCryptoPaymentData().GetValue()} {payment.Currency} {payment.GetCryptoPaymentData().GetPaymentId()}");
            var paymentData = (ZcashLikePaymentData)payment.GetCryptoPaymentData();
            var paymentMethod = invoice.GetPaymentMethod(payment.Network, ZcashPaymentType.Instance);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is ZcashLikeOnChainPaymentMethodDetails Zcash &&
                Zcash.Activated && 
                Zcash.GetPaymentDestination() == paymentData.GetDestination() &&
                paymentMethod.Calculate().Due > 0.0m)
            {
                var walletClient = _ZcashRpcProvider.WalletRpcClients[payment.Currency];

                var address = await walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>(
                    "create_address",
                    new CreateAddressRequest()
                    {
                        Label = $"btcpay invoice #{invoice.Id}",
                        AccountIndex = Zcash.AccountIndex
                    });
                Zcash.DepositAddress = address.Address;
                Zcash.AddressIndex = address.AddressIndex;
                await _invoiceRepository.NewPaymentDetails(invoice.Id, Zcash, payment.Network);
                _eventAggregator.Publish(
                    new InvoiceNewPaymentDetailsEvent(invoice.Id, Zcash, payment.GetPaymentMethodId()));
                paymentMethod.SetPaymentMethodDetails(Zcash);
                invoice.SetPaymentMethod(paymentMethod);
            }

            _eventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices)
        {
            if (!invoices.Any())
            {
                return;
            }

            var ZcashWalletRpcClient = _ZcashRpcProvider.WalletRpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);


            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllZcashLikePayments(entity, cryptoCode),
                    PaymentMethodDetails: entity.GetPaymentMethod(network, ZcashPaymentType.Instance)
                        .GetPaymentMethodDetails() as ZcashLikeOnChainPaymentMethodDetails))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: (ZcashLikePaymentData)entity.GetCryptoPaymentData(),
                            tuple.Invoice))
                ));

            var existingPaymentData = expandedInvoices.SelectMany(tuple => tuple.ExistingPayments);

            var accountToAddressQuery = new Dictionary<long, List<long>>();
            //create list of subaddresses to account to query the Zcash wallet
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
                datas => ZcashWalletRpcClient.SendCommandAsync<GetTransfersRequest, GetTransfersResponse>(
                    "get_transfers",
                    new GetTransfersRequest()
                    {
                        AccountIndex = datas.Key,
                        In = true,
                        SubaddrIndices = datas.Value.Distinct().ToList()
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


                    return HandlePaymentData(cryptoCode, transfer.Address, transfer.Amount, transfer.SubaddrIndex.Major,
                        transfer.SubaddrIndex.Minor, transfer.Txid, transfer.Confirmations, transfer.Height, invoice,
                        updatedPaymentEntities);
                }));
            }

            transferProcessingTasks.Add(
                _paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList()));
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

        private async Task OnNewBlock(string cryptoCode)
        {
            await UpdateAnyPendingZcashLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent() { CryptoCode = cryptoCode });
        }

        private async Task OnTransactionUpdated(string cryptoCode, string transactionHash)
        {
            var paymentMethodId = new PaymentMethodId(cryptoCode, ZcashPaymentType.Instance);
            var transfer = await _ZcashRpcProvider.WalletRpcClients[cryptoCode]
                .SendCommandAsync<GetTransferByTransactionIdRequest, GetTransferByTransactionIdResponse>(
                    "get_transfer_by_txid",
                    new GetTransferByTransactionIdRequest() { TransactionId = transactionHash });

            var paymentsToUpdate = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();

            //group all destinations of the tx together and loop through the sets
            foreach (var destination in transfer.Transfers.GroupBy(destination => destination.Address))
            {
                //find the invoice corresponding to this address, else skip
                var address = destination.Key + "#" + paymentMethodId;
                var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] { address })).FirstOrDefault();
                if (invoice == null)
                {
                    continue;
                }

                var index = destination.First().SubaddrIndex;

                await HandlePaymentData(cryptoCode,
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
                await _paymentService.UpdatePayments(paymentsToUpdate.Select(tuple => tuple.Payment).ToList());
                foreach (var valueTuples in paymentsToUpdate.GroupBy(entity => entity.invoice))
                {
                    if (valueTuples.Any())
                    {
                        _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                    }
                }
            }
        }

        private async Task HandlePaymentData(string cryptoCode, string address, long totalAmount, long subaccountIndex,
            long subaddressIndex,
            string txId, long confirmations, long blockHeight, InvoiceEntity invoice,
            BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            //construct the payment data
            var paymentData = new ZcashLikePaymentData()
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
            var alreadyExistingPaymentThatMatches = GetAllZcashLikePayments(invoice, cryptoCode)
                .Select(entity => (Payment: entity, PaymentData: entity.GetCryptoPaymentData()))
                .SingleOrDefault(c => c.PaymentData.GetPaymentId() == paymentData.GetPaymentId());

            //if it doesnt, add it and assign a new Zcashlike address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches.Payment == null)
            {
                var payment = await _paymentService.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                    paymentData, _networkProvider.GetNetwork<ZcashLikeSpecificBtcPayNetwork>(cryptoCode), true);
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

        private async Task UpdateAnyPendingZcashLikePayment(string cryptoCode)
        {
            var invoices = await _invoiceRepository.GetPendingInvoices();
            if (!invoices.Any())
                return;
            invoices = invoices.Where(entity => entity.GetPaymentMethod(new PaymentMethodId(cryptoCode, ZcashPaymentType.Instance))
                ?.GetPaymentMethodDetails().Activated is true).ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllZcashLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.GetPaymentMethodId() == new PaymentMethodId(cryptoCode, ZcashPaymentType.Instance));
        }
    }
}
#endif
