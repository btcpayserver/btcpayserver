#if ALTCOINS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BTCPayServer.Common.Altcoins.Chia.RPC;
using BTCPayServer.Common.Altcoins.Chia.RPC.Models;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.Payments;
using BTCPayServer.Services.Altcoins.Chia.RPC;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaListener : IHostedService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly ChiaRPCProvider _ChiaRpcProvider;
        private readonly ChiaLikeConfiguration _ChiaLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<ChiaListener> _logger;
        private readonly PaymentService _paymentService;
        private readonly CompositeDisposable leases = new CompositeDisposable();
        private readonly Channel<Func<Task>> _requests = Channel.CreateUnbounded<Func<Task>>();
        private CancellationTokenSource _Cts;

        public ChiaListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ChiaRPCProvider ChiaRpcProvider,
            ChiaLikeConfiguration ChiaLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<ChiaListener> logger,
            PaymentService paymentService)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _ChiaRpcProvider = ChiaRpcProvider;
            _ChiaLikeConfiguration = ChiaLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _paymentService = paymentService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_ChiaLikeConfiguration.ChiaLikeConfigurationItems.Any())
            {
                return Task.CompletedTask;
            }

            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<ChiaRPCProvider.ChiaDaemonStateChange>(e =>
            {
                if (_ChiaRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ = UpdateAnyPendingChiaLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));
            using var cts = new CancellationTokenSource(5000);

            _ = WorkThroughQueue(_Cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkThroughQueue(CancellationToken token)
        {
            while (await _requests.Reader.WaitToReadAsync(token) && _requests.Reader.TryRead(out var action))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await action.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError($"error with action item {e}");
                }
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.GetCryptoPaymentData().GetValue()} {payment.Currency} {payment.GetCryptoPaymentData().GetPaymentId()}");
            var paymentData = (ChiaLikePaymentData)payment.GetCryptoPaymentData();
            var paymentMethod = invoice.GetPaymentMethod(payment.Network, ChiaPaymentType.Instance);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is ChiaLikeOnChainPaymentMethodDetails Chia &&
                Chia.Activated &&
                Chia.GetPaymentDestination() == paymentData.GetDestination() &&
                paymentMethod.Calculate().Due > 0.0m)
            {
                var walletClient = _ChiaRpcProvider.WalletRpcClients[payment.Currency];

                // TODO Why does it need another address?
                var address = await walletClient.SendCommandAsync<GetNextAddressRequest, GetNextAddressResponse>(
                    "get_next_address",
                    new GetNextAddressRequest() { WalletId = Chia.WalletId });
                Chia.DepositAddress = address.Address;
                await _invoiceRepository.NewPaymentDetails(invoice.Id, Chia, payment.Network);
                _eventAggregator.Publish(
                    new InvoiceNewPaymentDetailsEvent(invoice.Id, Chia, payment.GetPaymentMethodId()));
                paymentMethod.SetPaymentMethodDetails(Chia);
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

            var ChiaWalletRpcClient = _ChiaRpcProvider.WalletRpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);


            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllChiaLikePayments(entity, cryptoCode),
                    PaymentMethodDetails: entity.GetPaymentMethod(network, ChiaPaymentType.Instance)
                        .GetPaymentMethodDetails() as ChiaLikeOnChainPaymentMethodDetails))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: (ChiaLikePaymentData)entity.GetCryptoPaymentData(),
                            tuple.Invoice))
                ));

            var existingPaymentData = expandedInvoices.SelectMany(tuple => tuple.ExistingPayments);

            var accountToAddressQuery = new Dictionary<int, List<string>>();
            //create list of addresses to account to query the Chia wallet
            foreach (var expandedInvoice in expandedInvoices)
            {
                var addressList =
                    accountToAddressQuery.GetValueOrDefault(expandedInvoice.PaymentMethodDetails.WalletId,
                        new List<string>());

                addressList.AddRange(
                    expandedInvoice.ExistingPayments.Select(tuple => tuple.PaymentData.Address));
                accountToAddressQuery.AddOrReplace(expandedInvoice.PaymentMethodDetails.WalletId, addressList);
            }

            var tasks = accountToAddressQuery.ToDictionary(datas => datas.Key,
                datas => ChiaWalletRpcClient.SendCommandAsync<GetTransactionsRequest, GetTransactionsResponse>(
                    "get_transactions",
                    new GetTransactionsRequest() { WalletId = datas.Key }));

            await Task.WhenAll(tasks.Values);

            var transactionProcessingTasks = new List<Task>();

            var updatedPaymentEntities = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
            foreach (var keyValuePair in tasks)
            {
                var transactions = keyValuePair.Value.Result.In;
                if (transactions == null)
                {
                    continue;
                }

                transactionProcessingTasks.AddRange(transactions.Select(transaction =>
                {
                    InvoiceEntity invoice = null;
                    var existingMatch = existingPaymentData.SingleOrDefault(tuple =>
                        tuple.PaymentData.Address == transaction.ToAddress &&
                        tuple.PaymentData.TransactionId == transaction.TransactionId);

                    if (existingMatch.Invoice != null)
                    {
                        invoice = existingMatch.Invoice;
                    }
                    else
                    {
                        var newMatch = expandedInvoices.SingleOrDefault(tuple =>
                            tuple.PaymentMethodDetails.GetPaymentDestination() == transaction.ToAddress);

                        if (newMatch.Invoice == null)
                        {
                            return Task.CompletedTask;
                        }

                        invoice = newMatch.Invoice;
                    }

                    var confirmations = Math.Max(_ChiaRpcProvider.Summaries[cryptoCode].WalletHeight -
                                                 transaction.ConfirmedAtHeight, 0);

                    return HandlePaymentData(cryptoCode, transaction.ToAddress, transaction.Amount,
                        transaction.TransactionId,
                        confirmations, transaction.ConfirmedAtHeight, invoice,
                        updatedPaymentEntities);
                }));
            }

            transactionProcessingTasks.Add(
                _paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList()));
            await Task.WhenAll(transactionProcessingTasks);
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
            await UpdateAnyPendingChiaLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent() { CryptoCode = cryptoCode });
        }

        private async Task HandlePaymentData(string cryptoCode, string address, ulong totalAmount,
            string txId, long confirmations, long blockHeight, InvoiceEntity invoice,
            BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            //construct the payment data
            var paymentData = new ChiaLikePaymentData()
            {
                Address = address,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                Amount = totalAmount,
                BlockHeight = blockHeight,
                Network = _networkProvider.GetNetwork(cryptoCode)
            };

            //check if this tx exists as a payment to this invoice already
            var alreadyExistingPaymentThatMatches = GetAllChiaLikePayments(invoice, cryptoCode)
                .Select(entity => (Payment: entity, PaymentData: entity.GetCryptoPaymentData()))
                .SingleOrDefault(c => c.PaymentData.GetPaymentId() == paymentData.GetPaymentId());

            //if it doesnt, add it and assign a new Chialike address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches.Payment == null)
            {
                var payment = await _paymentService.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                    paymentData, _networkProvider.GetNetwork<ChiaLikeSpecificBtcPayNetwork>(cryptoCode), true);
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

        private async Task UpdateAnyPendingChiaLikePayment(string cryptoCode)
        {
            var invoices = await _invoiceRepository.GetPendingInvoices();
            if (!invoices.Any())
                return;
            invoices = invoices.Where(entity => entity
                .GetPaymentMethod(new PaymentMethodId(cryptoCode, ChiaPaymentType.Instance))
                ?.GetPaymentMethodDetails().Activated is true).ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllChiaLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.GetPaymentMethodId() == new PaymentMethodId(cryptoCode, ChiaPaymentType.Instance));
        }
    }
}
#endif
