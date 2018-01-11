using NBXplorer;
using Microsoft.Extensions.Logging;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using BTCPayServer.Logging;
using System.Threading;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Hangfire;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using Microsoft.AspNetCore.Hosting;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.HostedServices
{
    public class InvoiceWatcher : IHostedService
    {
        class UpdateInvoiceContext
        {
            public UpdateInvoiceContext()
            {

            }

            public Dictionary<BTCPayNetwork, KnownState> KnownStates { get; set; }
            public Dictionary<BTCPayNetwork, KnownState> ModifiedKnownStates { get; set; } = new Dictionary<BTCPayNetwork, KnownState>();
            public InvoiceEntity Invoice { get; set; }
            public List<object> Events { get; set; } = new List<object>();

            bool _Dirty = false;
            public void MarkDirty()
            {
                _Dirty = true;
            }

            public bool Dirty => _Dirty;
        }

        InvoiceRepository _InvoiceRepository;
        EventAggregator _EventAggregator;
        BTCPayWalletProvider _WalletProvider;
        BTCPayNetworkProvider _NetworkProvider;

        public InvoiceWatcher(
            IHostingEnvironment env,
            BTCPayNetworkProvider networkProvider,
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayWalletProvider walletProvider)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _WalletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _NetworkProvider = networkProvider;
        }
        CompositeDisposable leases = new CompositeDisposable();

        async Task NotifyReceived(Script scriptPubKey, BTCPayNetwork network)
        {
            var invoice = await _InvoiceRepository.GetInvoiceIdFromScriptPubKey(scriptPubKey, network.CryptoCode);
            if (invoice != null)
                _WatchRequests.Add(invoice);
        }

        async Task NotifyBlock()
        {
            foreach (var invoice in await _InvoiceRepository.GetPendingInvoices())
            {
                _WatchRequests.Add(invoice);
            }
        }

        private async Task UpdateInvoice(string invoiceId, CancellationToken cancellation)
        {
            Dictionary<BTCPayNetwork, KnownState> changes = new Dictionary<BTCPayNetwork, KnownState>();
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId, true).ConfigureAwait(false);
                    if (invoice == null)
                        break;
                    var stateBefore = invoice.Status;
                    var updateContext = new UpdateInvoiceContext()
                    {
                        Invoice = invoice,
                        KnownStates = changes
                    };
                    await UpdateInvoice(updateContext).ConfigureAwait(false);
                    if (updateContext.Dirty)
                    {
                        await _InvoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.Status, invoice.ExceptionStatus).ConfigureAwait(false);
                        _EventAggregator.Publish(new InvoiceDataChangedEvent() { InvoiceId = invoice.Id });
                    }

                    var changed = stateBefore != invoice.Status;

                    foreach (var evt in updateContext.Events)
                    {
                        _EventAggregator.Publish(evt, evt.GetType());
                    }

                    foreach (var modifiedKnownState in updateContext.ModifiedKnownStates)
                    {
                        changes.AddOrReplace(modifiedKnownState.Key, modifiedKnownState.Value);
                    }

                    if (invoice.Status == "complete" ||
                       ((invoice.Status == "invalid" || invoice.Status == "expired") && invoice.MonitoringExpiration < DateTimeOffset.UtcNow))
                    {
                        if (await _InvoiceRepository.RemovePendingInvoice(invoice.Id).ConfigureAwait(false))
                            Logs.PayServer.LogInformation("Stopped watching invoice " + invoiceId);
                        break;
                    }

                    if (!changed || cancellation.IsCancellationRequested)
                        break;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
                    await Task.Delay(10000, cancellation).ConfigureAwait(false);
                }
            }
        }


        private async Task UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            if (invoice.Status == "new" && invoice.ExpirationTime < DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                await _InvoiceRepository.UnaffectAddress(invoice.Id);

                context.Events.Add(new InvoiceStatusChangedEvent(invoice, "expired"));
                invoice.Status = "expired";
            }

            var derivationStrategies = invoice.GetDerivationStrategies(_NetworkProvider).ToArray();
            var payments = await GetPaymentsWithTransaction(null, derivationStrategies, invoice);
            foreach (Task<NetworkCoins> coinsAsync in GetCoinsPerNetwork(context, invoice, derivationStrategies))
            {
                var coins = await coinsAsync;
                if (coins.TimestampedCoins.Length == 0)
                    continue;
                bool dirtyAddress = false;
                if (coins.State != null)
                    context.ModifiedKnownStates.AddOrReplace(coins.Wallet.Network, coins.State);
                var alreadyAccounted = new HashSet<OutPoint>(invoice.GetPayments(coins.Wallet.Network).Select(p => p.Outpoint));

                foreach (var coin in coins.TimestampedCoins.Where(c => !alreadyAccounted.Contains(c.Coin.Outpoint)))
                {
                    var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin.DateTime, coin.Coin, coins.Wallet.Network.CryptoCode).ConfigureAwait(false);
#pragma warning disable CS0618
                    invoice.Payments.Add(payment);
#pragma warning restore CS0618
                    alreadyAccounted.Add(coin.Coin.Outpoint);
                    context.Events.Add(new InvoicePaymentEvent(invoice.Id));
                    dirtyAddress = true;
                }
                if (dirtyAddress)
                {
                    payments = await GetPaymentsWithTransaction(payments, derivationStrategies, invoice);
                }
                var network = coins.Wallet.Network;
                var cryptoData = invoice.GetCryptoData(network, _NetworkProvider);
                var cryptoDataAll = invoice.GetCryptoData(_NetworkProvider);
                var accounting = cryptoData.Calculate();

                if (invoice.Status == "new" || invoice.Status == "expired")
                {
                    var totalPaid = payments.Select(p => p.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();
                    if (totalPaid >= accounting.TotalDue)
                    {
                        if (invoice.Status == "new")
                        {
                            context.Events.Add(new InvoiceStatusChangedEvent(invoice, "paid"));
                            invoice.Status = "paid";
                            invoice.ExceptionStatus = null;
                            await _InvoiceRepository.UnaffectAddress(invoice.Id);
                            context.MarkDirty();
                        }
                        else if (invoice.Status == "expired")
                        {
                            invoice.ExceptionStatus = "paidLate";
                            context.MarkDirty();
                        }
                    }

                    if (totalPaid > accounting.TotalDue && invoice.ExceptionStatus != "paidOver")
                    {
                        invoice.ExceptionStatus = "paidOver";
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.MarkDirty();
                    }

                    if (totalPaid < accounting.TotalDue && invoice.GetPayments().Count != 0 && invoice.ExceptionStatus != "paidPartial")
                    {
                        invoice.ExceptionStatus = "paidPartial";
                        context.MarkDirty();
                        if (dirtyAddress)
                        {
                            var address = await coins.Wallet.ReserveAddressAsync(coins.Strategy);
                            Logs.PayServer.LogInformation("Generate new " + address);
                            await _InvoiceRepository.NewAddress(invoice.Id, address, network);
                        }
                    }
                }

                if (invoice.Status == "paid")
                {
                    IEnumerable<AccountedPaymentEntity> transactions = payments;
                    if (invoice.SpeedPolicy == SpeedPolicy.HighSpeed)
                    {
                        transactions = transactions.Where(t => t.Confirmations >= 1 || !t.Transaction.RBF);
                    }
                    else if (invoice.SpeedPolicy == SpeedPolicy.MediumSpeed)
                    {
                        transactions = transactions.Where(t => t.Confirmations >= 1);
                    }
                    else if (invoice.SpeedPolicy == SpeedPolicy.LowSpeed)
                    {
                        transactions = transactions.Where(t => t.Confirmations >= 6);
                    }

                    var totalConfirmed = transactions.Select(t => t.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();

                    if (// Is after the monitoring deadline
                       (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                       &&
                       // And not enough amount confirmed
                       (totalConfirmed < accounting.TotalDue))
                    {
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.Events.Add(new InvoiceStatusChangedEvent(invoice, "invalid"));
                        invoice.Status = "invalid";
                        context.MarkDirty();
                    }
                    else if (totalConfirmed >= accounting.TotalDue)
                    {
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.Events.Add(new InvoiceStatusChangedEvent(invoice, "confirmed"));
                        invoice.Status = "confirmed";
                        context.MarkDirty();
                    }
                }

                if (invoice.Status == "confirmed")
                {
                    IEnumerable<AccountedPaymentEntity> transactions = payments;
                    transactions = transactions.Where(t => t.Confirmations >= 6);
                    var totalConfirmed = transactions.Select(t => t.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();
                    if (totalConfirmed >= accounting.TotalDue)
                    {
                        context.Events.Add(new InvoiceStatusChangedEvent(invoice, "complete"));
                        invoice.Status = "complete";
                        context.MarkDirty();
                    }
                }
            }
        }

        private IEnumerable<Task<NetworkCoins>> GetCoinsPerNetwork(UpdateInvoiceContext context, InvoiceEntity invoice, DerivationStrategy[] strategies)
        {
            return strategies
                                .Select(d => (Wallet: _WalletProvider.GetWallet(d.Network),
                                              Network: d.Network,
                                              Strategy: d.DerivationStrategyBase))
                                .Where(d => d.Wallet != null)
                                .Select(d => (Network: d.Network,
                                              Coins: d.Wallet.GetCoins(d.Strategy, context.KnownStates.TryGet(d.Network))))
                                .Select(async d =>
                                {
                                    var coins = await d.Coins;
                                    // Keep only coins from the invoice
                                    coins.TimestampedCoins = coins.TimestampedCoins.Where(c => invoice.AvailableAddressHashes.Contains(c.Coin.ScriptPubKey.Hash.ToString() + d.Network.CryptoCode)).ToArray();
                                    return coins;
                                })
                                .ToArray();
        }


        class AccountedPaymentEntities : List<AccountedPaymentEntity>
        {
            public AccountedPaymentEntities(AccountedPaymentEntities existing)
            {
                if (existing != null)
                    _Transactions = existing._Transactions;
            }

            Dictionary<uint256, TransactionResult> _Transactions = new Dictionary<uint256, TransactionResult>();

            public void AddToCache(IEnumerable<TransactionResult> transactions)
            {
                foreach (var tx in transactions)
                    _Transactions.TryAdd(tx.Transaction.GetHash(), tx);
            }
            public TransactionResult GetTransaction(uint256 txId)
            {
                _Transactions.TryGetValue(txId, out TransactionResult result);
                return result;
            }

            internal IEnumerable<TransactionResult> GetTransactions()
            {
                return _Transactions.Values;
            }
        }
        private async Task<AccountedPaymentEntities> GetPaymentsWithTransaction(AccountedPaymentEntities previous, DerivationStrategy[] derivations, InvoiceEntity invoice)
        {
            List<PaymentEntity> updatedPaymentEntities = new List<PaymentEntity>();
            AccountedPaymentEntities accountedPayments = new AccountedPaymentEntities(previous);
            foreach (var network in derivations.Select(d => d.Network))
            {
                var wallet = _WalletProvider.GetWallet(network);
                if (wallet == null)
                    continue;

                var hashesToFetch = new HashSet<uint256>(invoice
                    .GetPayments(network)
                    .Select(t => t.Outpoint.Hash)
                    .Where(h => accountedPayments?.GetTransaction(h) == null)
                    .ToList());


                if (hashesToFetch.Count > 0)
                {
                    accountedPayments.AddToCache((await wallet.GetTransactions(hashesToFetch.ToArray())).Select(t => t.Value));
                }
                var conflicts = GetConflicts(accountedPayments.GetTransactions());
                foreach (var payment in invoice.GetPayments(network))
                {
                    TransactionResult tx = accountedPayments.GetTransaction(payment.Outpoint.Hash);
                    if (tx == null)
                        continue;

                    AccountedPaymentEntity accountedPayment = new AccountedPaymentEntity()
                    {
                        Confirmations = tx.Confirmations,
                        Transaction = tx.Transaction,
                        Payment = payment
                    };
                    var txId = accountedPayment.Transaction.GetHash();
                    var txConflict = conflicts.GetConflict(txId);
                    var accounted = txConflict == null || txConflict.IsWinner(txId);
                    if (accounted != payment.Accounted)
                    {
                        updatedPaymentEntities.Add(payment);
                        payment.Accounted = accounted;
                    }

                    if (accounted)
                        accountedPayments.Add(accountedPayment);
                }
            }
            await _InvoiceRepository.UpdatePayments(updatedPaymentEntities);
            return accountedPayments;
        }


        class TransactionConflict
        {
            public Dictionary<uint256, TransactionResult> Transactions { get; set; } = new Dictionary<uint256, TransactionResult>();


            uint256 _Winner;
            public bool IsWinner(uint256 txId)
            {
                if (_Winner == null)
                {
                    var confirmed = Transactions.FirstOrDefault(t => t.Value.Confirmations >= 1);
                    if (!confirmed.Equals(default(KeyValuePair<uint256, TransactionResult>)))
                    {
                        _Winner = confirmed.Key;
                    }
                    else
                    {
                        // Take the most recent (bitcoin node would not forward a conflict without a successfull RBF)
                        _Winner = Transactions
                                .OrderByDescending(t => t.Value.Timestamp)
                                .First()
                                .Key;
                    }
                }
                return _Winner == txId;
            }
        }
        class TransactionConflicts : List<TransactionConflict>
        {
            public TransactionConflicts(IEnumerable<TransactionConflict> collection) : base(collection)
            {

            }

            public TransactionConflict GetConflict(uint256 txId)
            {
                return this.FirstOrDefault(c => c.Transactions.ContainsKey(txId));
            }
        }
        private TransactionConflicts GetConflicts(IEnumerable<TransactionResult> transactions)
        {
            Dictionary<OutPoint, TransactionConflict> conflictsByOutpoint = new Dictionary<OutPoint, TransactionConflict>();
            foreach (var tx in transactions)
            {
                var hash = tx.Transaction.GetHash();
                foreach (var input in tx.Transaction.Inputs)
                {
                    TransactionConflict conflict = new TransactionConflict();
                    if (!conflictsByOutpoint.TryAdd(input.PrevOut, conflict))
                    {
                        conflict = conflictsByOutpoint[input.PrevOut];
                    }
                    if (!conflict.Transactions.ContainsKey(hash))
                        conflict.Transactions.Add(hash, tx);
                }
            }
            return new TransactionConflicts(conflictsByOutpoint.Where(c => c.Value.Transactions.Count > 1).Select(c => c.Value));
        }

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
            }
        }

        private void Watch(string invoiceId)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            _WatchRequests.Add(invoiceId);
        }

        BlockingCollection<string> _WatchRequests = new BlockingCollection<string>(new ConcurrentQueue<string>());

        Task _Poller;
        Task _Loop;
        CancellationTokenSource _Cts;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _Poller = StartPoller(_Cts.Token);
            _Loop = StartLoop(_Cts.Token);

            leases.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async b => { await NotifyBlock(); }));
            leases.Add(_EventAggregator.Subscribe<Events.TxOutReceivedEvent>(async b => { await NotifyReceived(b.ScriptPubKey, b.Network); }));
            leases.Add(_EventAggregator.Subscribe<Events.InvoiceCreatedEvent>(b => { Watch(b.InvoiceId); }));

            return Task.CompletedTask;
        }


        private async Task StartPoller(CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var pending in await _InvoiceRepository.GetPendingInvoices())
                        {
                            _WatchRequests.Add(pending);
                        }
                        await Task.Delay(PollInterval, cancellation);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in InvoiceWatcher poller");
                        await Task.Delay(PollInterval, cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }

        async Task StartLoop(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation("Start watching invoices");
            await Task.Delay(1).ConfigureAwait(false); // Small hack so that the caller does not block on GetConsumingEnumerable
            ConcurrentDictionary<string, Task> executing = new ConcurrentDictionary<string, Task>();
            try
            {
                foreach (var item in _WatchRequests.GetConsumingEnumerable(cancellation))
                {
                    var task = executing.GetOrAdd(item, async i =>
                    {
                        try
                        {
                            await UpdateInvoice(i, cancellation);
                        }
                        catch (Exception ex) when (!cancellation.IsCancellationRequested)
                        {
                            Logs.PayServer.LogCritical(ex, $"Error in the InvoiceWatcher loop (Invoice {item})");
                            await Task.Delay(2000, cancellation);
                        }
                        finally { executing.TryRemove(item, out Task useless); }
                    });
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                await Task.WhenAll(executing.Values);
            }
            Logs.PayServer.LogInformation("Stop watching invoices");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            return Task.WhenAll(_Poller, _Loop);
        }
    }
}
