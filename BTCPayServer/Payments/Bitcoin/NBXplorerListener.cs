﻿using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using System.Collections.Concurrent;
using BTCPayServer.Controllers;
using NBXplorer.DerivationStrategy;
using BTCPayServer.Events;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using NBitcoin;
using NBXplorer.Models;
using BTCPayServer.Payments;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.PayJoin;
using NBitcoin.Altcoins.Elements;
using NBitcoin.RPC;
using BTCPayServer;

namespace BTCPayServer.Payments.Bitcoin
{
    /// <summary>
    /// This class listener NBXplorer instances to detect incoming on-chain, bitcoin like payment
    /// </summary>
    public class NBXplorerListener : IHostedService
    {
        EventAggregator _Aggregator;
        private readonly PayJoinRepository _payJoinRepository;
        ExplorerClientProvider _ExplorerClients;
        IHostApplicationLifetime _Lifetime;
        InvoiceRepository _InvoiceRepository;
        private TaskCompletionSource<bool> _RunningTask;
        private CancellationTokenSource _Cts;
        BTCPayWalletProvider _Wallets;
        public NBXplorerListener(ExplorerClientProvider explorerClients,
                                BTCPayWalletProvider wallets,
                                InvoiceRepository invoiceRepository,
                                EventAggregator aggregator,
                                PayJoinRepository payjoinRepository,
                                IHostApplicationLifetime lifetime)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _Wallets = wallets;
            _InvoiceRepository = invoiceRepository;
            _ExplorerClients = explorerClients;
            _Aggregator = aggregator;
            _payJoinRepository = payjoinRepository;
            _Lifetime = lifetime;
        }

        CompositeDisposable leases = new CompositeDisposable();
        ConcurrentDictionary<string, WebsocketNotificationSession> _SessionsByCryptoCode = new ConcurrentDictionary<string, WebsocketNotificationSession>();
        private Timer _ListenPoller;

        TimeSpan _PollInterval;
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                _PollInterval = value;
                if (_ListenPoller != null)
                {
                    _ListenPoller.Change(0, (int)value.TotalMilliseconds);
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _RunningTask = new TaskCompletionSource<bool>();
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leases.Add(_Aggregator.Subscribe<Events.NBXplorerStateChangedEvent>(async nbxplorerEvent =>
            {
                if (nbxplorerEvent.NewState == NBXplorerState.Ready)
                {
                    var wallet = _Wallets.GetWallet(nbxplorerEvent.Network);
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        await Listen(wallet);
                    }
                }
            }));

            _ListenPoller = new Timer(async s =>
            {
                foreach (var wallet in _Wallets.GetWallets())
                {
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        await Listen(wallet);
                    }
                }
            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        private async Task Listen(BTCPayWallet wallet)
        {
            var network = wallet.Network;
            bool cleanup = false;
            try
            {
                if (_SessionsByCryptoCode.ContainsKey(network.CryptoCode))
                    return;
                var client = _ExplorerClients.GetExplorerClient(network);
                if (client == null)
                    return;
                if (_Cts.IsCancellationRequested)
                    return;
                var session = await client.CreateWebsocketNotificationSessionAsync(_Cts.Token).ConfigureAwait(false);
                if (!_SessionsByCryptoCode.TryAdd(network.CryptoCode, session))
                {
                    await session.DisposeAsync();
                    return;
                }
                cleanup = true;

                using (session)
                {
                    await session.ListenNewBlockAsync(_Cts.Token).ConfigureAwait(false);
                    await session.ListenAllDerivationSchemesAsync(cancellation: _Cts.Token).ConfigureAwait(false);

                    Logs.PayServer.LogInformation($"{network.CryptoCode}: Checking if any pending invoice got paid while offline...");
                    int paymentCount = await FindPaymentViaPolling(wallet, network);
                    Logs.PayServer.LogInformation($"{network.CryptoCode}: {paymentCount} payments happened while offline");

                    Logs.PayServer.LogInformation($"Connected to WebSocket of NBXplorer ({network.CryptoCode})");
                    while (!_Cts.IsCancellationRequested)
                    {
                        var newEvent = await session.NextEventAsync(_Cts.Token).ConfigureAwait(false);
                        switch (newEvent)
                        {
                            case NBXplorer.Models.NewBlockEvent evt:
                                await Task.WhenAll((await _InvoiceRepository.GetPendingInvoices())
                                    .Select(invoiceId => UpdatePaymentStates(wallet, invoiceId))
                                    .ToArray());
                                _Aggregator.Publish(new Events.NewBlockEvent() { CryptoCode = evt.CryptoCode });
                                break;
                            case NBXplorer.Models.NewTransactionEvent evt:
                                wallet.InvalidateCache(evt.DerivationStrategy);

                                foreach (var output in network.GetValidOutputs(evt))
                                {
                                    var key = output.Item1.ScriptPubKey.Hash + "#" + network.CryptoCode.ToUpperInvariant();
                                    var invoice = (await _InvoiceRepository.GetInvoicesFromAddresses(new[] { key })).FirstOrDefault();
                                    if (invoice != null)
                                    {
                                        var address = network.NBXplorerNetwork.CreateAddress(evt.DerivationStrategy,
                                            output.Item1.KeyPath, output.Item1.ScriptPubKey);

                                        var paymentData = new BitcoinLikePaymentData(address,
                                            output.matchedOutput.Value, output.outPoint,
                                            evt.TransactionData.Transaction.RBF);
                                        
                                        var alreadyExist = invoice.GetAllBitcoinPaymentData().Where(c => c.GetPaymentId() == paymentData.GetPaymentId()).Any();
                                        if (!alreadyExist)
                                        {
                                            var payment = await _InvoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow, paymentData, network);
                                            if (payment != null)
                                                await ReceivedPayment(wallet, invoice, payment, evt.DerivationStrategy);
                                        }
                                        else
                                        {
                                            await UpdatePaymentStates(wallet, invoice.Id);
                                        }
                                    }

                                }
                                _Aggregator.Publish(new NewOnChainTransactionEvent()
                                {
                                    CryptoCode = wallet.Network.CryptoCode,
                                    NewTransactionEvent = evt
                                });

                                break;
                            default:
                                Logs.PayServer.LogWarning("Received unknown message from NBXplorer");
                                break;
                        }
                    }
                }
            }
            catch when (_Cts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, $"Error while connecting to WebSocket of NBXplorer ({network.CryptoCode})");
            }
            finally
            {
                if (cleanup)
                {
                    Logs.PayServer.LogInformation($"Disconnected from WebSocket of NBXplorer ({network.CryptoCode})");
                    _SessionsByCryptoCode.TryRemove(network.CryptoCode, out WebsocketNotificationSession unused);
                    if (_SessionsByCryptoCode.Count == 0 && _Cts.IsCancellationRequested)
                    {
                        _RunningTask.TrySetResult(true);
                    }
                }
            }
        }

        async Task<InvoiceEntity> UpdatePaymentStates(BTCPayWallet wallet, string invoiceId)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId, false);
            if (invoice == null)
                return null;
            List<PaymentEntity> updatedPaymentEntities = new List<PaymentEntity>();
            var transactions = await wallet.GetTransactions(invoice.GetAllBitcoinPaymentData()
                    .Select(p => p.Outpoint.Hash)
                    .ToArray(), true);
            bool? originalPJBroadcasted = null;
            bool? originalPJBroadcastable = null;
            bool? cjPJBroadcasted = null;
            OutPoint[] ourPJOutpoints = null;
            foreach (var payment in invoice.GetPayments(wallet.Network))
            {
                if (payment.GetPaymentMethodId().PaymentType != PaymentTypes.BTCLike)
                    continue;
                var paymentData = (BitcoinLikePaymentData)payment.GetCryptoPaymentData();
                if (!transactions.TryGetValue(paymentData.Outpoint.Hash, out TransactionResult tx))
                    continue;

                bool accounted = true;

                if (tx.Confirmations == 0 || tx.Confirmations == -1)
                {
                    // Let's check if it was orphaned by broadcasting it again
                    var explorerClient = _ExplorerClients.GetExplorerClient(wallet.Network);
                    try
                    {
                        var result = await explorerClient.BroadcastAsync(tx.Transaction, testMempoolAccept: tx.Confirmations == -1, _Cts.Token);
                        accounted = result.Success ||
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN ||
                                    !(
                                    // Happen if a blocks mined a replacement
                                    // Or if the tx is a double spend of something already in the mempool without rbf
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_ERROR ||
                                    // Happen if RBF is on and fee insufficient
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_REJECTED);
                        if (!accounted && payment.Accounted && tx.Confirmations != -1)
                        {
                            Logs.PayServer.LogInformation($"{wallet.Network.CryptoCode}: The transaction {tx.TransactionHash} has been replaced.");
                        }
                        if (paymentData.PayjoinInformation is PayjoinInformation pj)
                        {
                            ourPJOutpoints = pj.OurOutpoints;
                            switch (pj.Type)
                            {
                                case PayjoinTransactionType.Original:
                                    originalPJBroadcasted = accounted && tx.Confirmations >= 0;
                                    originalPJBroadcastable = accounted;
                                    break;
                                case PayjoinTransactionType.Coinjoin:
                                    cjPJBroadcasted = accounted && tx.Confirmations >= 0;
                                    break;
                            }
                        }
                    }
                    // RPC might be unavailable, we can't check double spend so let's assume there is none
                    catch
                    {

                    }
                }

                bool updated = false;
                if (accounted != payment.Accounted)
                {
                    updated = true;
                    payment.Accounted = accounted;
                }

                if (paymentData.ConfirmationCount != tx.Confirmations)
                {
                    if (wallet.Network.MaxTrackedConfirmation >= paymentData.ConfirmationCount)
                    {
                        paymentData.ConfirmationCount = tx.Confirmations;
                        payment.SetCryptoPaymentData(paymentData);
                        updated = true;
                    }
                }

                // if needed add invoice back to pending to track number of confirmations
                if (paymentData.ConfirmationCount < wallet.Network.MaxTrackedConfirmation)
                    await _InvoiceRepository.AddPendingInvoiceIfNotPresent(invoice.Id);

                if (updated)
                    updatedPaymentEntities.Add(payment);
            }

            // If the origin tx of a payjoin has been broadcasted, then we know we can
            // reuse our outpoint for another PJ
            if (originalPJBroadcasted is true ||
                // If the original tx is not broadcastable anymore and nor does the coinjoin
                // reuse our outpoint for another PJ
                (originalPJBroadcastable is false && !(cjPJBroadcasted is true)))
            {
                await _payJoinRepository.TryUnlock(ourPJOutpoints);
            }
            await _InvoiceRepository.UpdatePayments(updatedPaymentEntities);
            if (updatedPaymentEntities.Count != 0)
                _Aggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
            return invoice;
        }

        private async Task<int> FindPaymentViaPolling(BTCPayWallet wallet, BTCPayNetwork network)
        {
            int totalPayment = 0;
            var invoices = await _InvoiceRepository.GetPendingInvoices();
            foreach (var invoiceId in invoices)
            {
                var invoice = await _InvoiceRepository.GetInvoice(invoiceId, true);
                if (invoice == null)
                    continue;
                var alreadyAccounted = invoice.GetAllBitcoinPaymentData().Select(p => p.Outpoint).ToHashSet();
                var strategy = GetDerivationStrategy(invoice, network);
                if (strategy == null)
                    continue;
                var cryptoId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
                var paymentMethod = invoice.GetPaymentMethod(cryptoId).GetPaymentMethodDetails() as BitcoinLikeOnChainPaymentMethod;
                
                if (!invoice.Support(cryptoId))
                    continue;
                var coins = (await wallet.GetUnspentCoins(strategy))
                             .Where(c => invoice.AvailableAddressHashes.Contains(c.ScriptPubKey.Hash.ToString() + cryptoId))
                             .ToArray();
                foreach (var coin in coins.Where(c => !alreadyAccounted.Contains(c.OutPoint)))
                {
                    var transaction = await wallet.GetTransactionAsync(coin.OutPoint.Hash);

                    var address = network.NBXplorerNetwork.CreateAddress(strategy, coin.KeyPath, coin.ScriptPubKey);

                    var paymentData = new BitcoinLikePaymentData(address, coin.Value, coin.OutPoint,
                        transaction.Transaction.RBF);
                    
                    var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin.Timestamp, paymentData, network).ConfigureAwait(false);
                    alreadyAccounted.Add(coin.OutPoint);
                    if (payment != null)
                    {
                        invoice = await ReceivedPayment(wallet, invoice, payment, strategy);
                        if (invoice == null)
                            continue;
                        totalPayment++;
                    }
                }
            }
            return totalPayment;
        }

        private DerivationStrategyBase GetDerivationStrategy(InvoiceEntity invoice, BTCPayNetworkBase network)
        {
            return invoice.GetSupportedPaymentMethod<DerivationSchemeSettings>(new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike))
                          .Select(d => d.AccountDerivation)
                          .FirstOrDefault();
        }

        private async Task<InvoiceEntity> ReceivedPayment(BTCPayWallet wallet, InvoiceEntity invoice, PaymentEntity payment, DerivationStrategyBase strategy)
        {
            var paymentData = (BitcoinLikePaymentData)payment.GetCryptoPaymentData();
            invoice = (await UpdatePaymentStates(wallet, invoice.Id));
            if (invoice == null)
                return null;
            var paymentMethod = invoice.GetPaymentMethod(wallet.Network, PaymentTypes.BTCLike);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is BitcoinLikeOnChainPaymentMethod btc &&
                btc.GetDepositAddress(wallet.Network.NBitcoinNetwork).ScriptPubKey == paymentData.ScriptPubKey &&
                paymentMethod.Calculate().Due > Money.Zero)
            {
                var address = await wallet.ReserveAddressAsync(strategy);
                btc.DepositAddress = address.Address.ToString();
                await _InvoiceRepository.NewAddress(invoice.Id, btc, wallet.Network);
                _Aggregator.Publish(new InvoiceNewAddressEvent(invoice.Id, btc.DepositAddress, wallet.Network));
                paymentMethod.SetPaymentMethodDetails(btc);
                invoice.SetPaymentMethod(paymentMethod);
            }
            wallet.InvalidateCache(strategy);
            _Aggregator.Publish(new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) { Payment = payment });
            return invoice;
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_Cts != null)
            {
                leases.Dispose();
                _Cts.Cancel();
                await Task.WhenAny(_RunningTask.Task, Task.Delay(-1, cancellationToken));
                Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
            }
        }
    }
}
