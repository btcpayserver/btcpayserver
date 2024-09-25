using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.RPC;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Services.Altcoins.Monero.Utils;
using BTCPayServer.Services.Altcoins.Zcash.Utils;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;

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
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly PaymentService _paymentService;
        private readonly CompositeDisposable leases = new CompositeDisposable();
        private readonly Queue<Func<CancellationToken, Task>> taskQueue = new Queue<Func<CancellationToken, Task>>();
        private CancellationTokenSource _Cts;

        public MoneroListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            MoneroRPCProvider moneroRpcProvider,
            MoneroLikeConfiguration moneroLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<MoneroListener> logger,
            PaymentMethodHandlerDictionary handlers,
            InvoiceActivator invoiceActivator,
            PaymentService paymentService)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _moneroRpcProvider = moneroRpcProvider;
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _handlers = handlers;
            _invoiceActivator = invoiceActivator;
            _paymentService = paymentService;
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
            _ = WorkThroughQueue(_Cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkThroughQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (taskQueue.TryDequeue(out var t))
                {
                    try
                    {

                        await t.Invoke(token);
                    }
                    catch (Exception e)
                    {

                        _logger.LogError($"error with queue item", e);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
            }
        }

        private void OnMoneroEvent(MoneroEvent obj)
        {
            if (!_moneroRpcProvider.IsAvailable(obj.CryptoCode))
            {
                return;
            }

            if (!string.IsNullOrEmpty(obj.BlockHash))
            {
                taskQueue.Enqueue(token => OnNewBlock(obj.CryptoCode));
            }

            if (!string.IsNullOrEmpty(obj.TransactionHash))
            {
                taskQueue.Enqueue(token => OnTransactionUpdated(obj.CryptoCode, obj.TransactionHash));
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);

            if (prompt != null &&
                prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
                invoice = await _invoiceRepository.GetInvoice(invoice.Id);
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

            var moneroWalletRpcClient = _moneroRpcProvider.WalletRpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);
            var paymentId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (MoneroLikePaymentMethodHandler)_handlers[paymentId];

            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllMoneroLikePayments(entity, cryptoCode),
                    Prompt: entity.GetPaymentPrompt(paymentId),
                    PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(paymentId).Details)))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    tuple.Prompt,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details),
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
                        tuple.Payment.Destination == transfer.Address &&
                        tuple.PaymentData.TransactionId == transfer.Txid);

                    if (existingMatch.Invoice != null)
                    {
                        invoice = existingMatch.Invoice;
                    }
                    else
                    {
                        var newMatch = expandedInvoices.SingleOrDefault(tuple =>
                            tuple.Prompt.Destination == transfer.Address);

                        if (newMatch.Invoice == null)
                        {
                            return Task.CompletedTask;
                        }

                        invoice = newMatch.Invoice;
                    }


                    return HandlePaymentData(cryptoCode, transfer.Address, transfer.Amount, transfer.SubaddrIndex.Major,
                        transfer.SubaddrIndex.Minor, transfer.Txid, transfer.Confirmations, transfer.Height, transfer.UnlockTime,invoice,
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
            await UpdateAnyPendingMoneroLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent() { CryptoCode = cryptoCode });
        }

        private async Task OnTransactionUpdated(string cryptoCode, string transactionHash)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var transfer = await _moneroRpcProvider.WalletRpcClients[cryptoCode]
                .SendCommandAsync<GetTransferByTransactionIdRequest, GetTransferByTransactionIdResponse>(
                    "get_transfer_by_txid",
                    new GetTransferByTransactionIdRequest() { TransactionId = transactionHash });

            var paymentsToUpdate = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();

            //group all destinations of the tx together and loop through the sets
            foreach (var destination in transfer.Transfers.GroupBy(destination => destination.Address))
            {
                //find the invoice corresponding to this address, else skip
                var invoice = await _invoiceRepository.GetInvoiceFromAddress(paymentMethodId, destination.Key);
                if (invoice == null)
                    continue;

                var index = destination.First().SubaddrIndex;

                await HandlePaymentData(cryptoCode,
                    destination.Key,
                    destination.Sum(destination1 => destination1.Amount),
                    index.Major,
                    index.Minor,
                    transfer.Transfer.Txid,
                    transfer.Transfer.Confirmations,
                    transfer.Transfer.Height
                    , transfer.Transfer.UnlockTime,invoice, paymentsToUpdate);
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
            string txId, long confirmations, long blockHeight, long locktime, InvoiceEntity invoice,
            BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (MoneroLikePaymentMethodHandler)_handlers[pmi];
            var promptDetails = handler.ParsePaymentPromptDetails(invoice.GetPaymentPrompt(pmi).Details);
            var details = new MoneroLikePaymentData()
            {
                SubaccountIndex = subaccountIndex,
                SubaddressIndex = subaddressIndex,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                BlockHeight = blockHeight,
                LockTime = locktime,
                InvoiceSettledConfirmationThreshold = promptDetails.InvoiceSettledConfirmationThreshold
            };
            var paymentData = new Data.PaymentData()
            {
                Status = GetStatus(details, invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing,
                Amount = MoneroMoney.Convert(totalAmount),
                Created = DateTimeOffset.UtcNow,
                Id = $"{txId}#{subaccountIndex}#{subaddressIndex}",
                Currency = network.CryptoCode,
                InvoiceDataId = invoice.Id,
            }.Set(invoice, handler, details);


            //check if this tx exists as a payment to this invoice already
            var alreadyExistingPaymentThatMatches = GetAllMoneroLikePayments(invoice, cryptoCode)
                .Select(entity => (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details)))
                .SingleOrDefault(c => c.Payment.PaymentMethodId == pmi);

            //if it doesnt, add it and assign a new monerolike address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches.Payment == null)
            {
                var payment = await _paymentService.AddPayment(paymentData, [txId]);
                if (payment != null)
                    await ReceivedPayment(invoice, payment);
            }
            else
            {
                //else update it with the new data
                alreadyExistingPaymentThatMatches.PaymentData = details;
                alreadyExistingPaymentThatMatches.Payment.Details = JToken.FromObject(paymentData, handler.Serializer);
                paymentsToUpdate.Add((alreadyExistingPaymentThatMatches.Payment, invoice));
            }
        }

        private bool GetStatus(MoneroLikePaymentData details, SpeedPolicy speedPolicy)
            => ConfirmationsRequired(details, speedPolicy) <= details.ConfirmationCount;

        public static long ConfirmationsRequired(MoneroLikePaymentData details, SpeedPolicy speedPolicy)
       => (details, speedPolicy) switch
       {
           (_, _) when details.ConfirmationCount < details.LockTime => details.LockTime - details.ConfirmationCount,
           ({ InvoiceSettledConfirmationThreshold: long v }, _) => v,
           (_, SpeedPolicy.HighSpeed) => 0,
           (_, SpeedPolicy.MediumSpeed) => 1,
           (_, SpeedPolicy.LowMediumSpeed) => 2,
           (_, SpeedPolicy.LowSpeed) => 6,
           _ => 6,
       };


        private async Task UpdateAnyPendingMoneroLikePayment(string cryptoCode)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var invoices = await _invoiceRepository.GetMonitoredInvoices(paymentMethodId);
            if (!invoices.Any())
                return;
            invoices = invoices.Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true).ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllMoneroLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        }
    }
}
