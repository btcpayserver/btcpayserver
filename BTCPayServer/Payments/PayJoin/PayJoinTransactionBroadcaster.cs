using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using Microsoft.Extensions.Hosting;
using NBXplorer;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayJoinTransactionBroadcaster : IHostedService
    {
        private readonly TimeSpan
            BroadcastAfter =
                TimeSpan.FromMinutes(
                    5); // The spec mentioned to give a few mins(1-2), but i don't think it took under consideration the time taken to re-sign inputs with interactive methods( multisig, Hardware wallets, etc). I think 5 mins might be ok.

        private readonly EventAggregator _eventAggregator;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly PayJoinStateProvider _payJoinStateProvider;

        private CompositeDisposable leases = new CompositeDisposable();

        public PayJoinTransactionBroadcaster(
            EventAggregator eventAggregator,
            ExplorerClientProvider explorerClientProvider,
            PayJoinStateProvider payJoinStateProvider)
        {
            _eventAggregator = eventAggregator;
            _explorerClientProvider = explorerClientProvider;
            _payJoinStateProvider = payJoinStateProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var loadCoins = _payJoinStateProvider.LoadCoins();
            //if the wallet was updated, we need to remove the state as the utxos no longer fit
            leases.Add(_eventAggregator.Subscribe<WalletChangedEvent>(evt =>
                _payJoinStateProvider.RemoveState(evt.WalletId)));

            leases.Add(_eventAggregator.Subscribe<NewOnChainTransactionEvent>(txEvent =>
            {
                if (!txEvent.NewTransactionEvent.Outputs.Any())
                {
                    return;
                }

                var relevantStates =
                    _payJoinStateProvider.Get(txEvent.CryptoCode, txEvent.NewTransactionEvent.DerivationStrategy);

                foreach (var relevantState in relevantStates)
                {
                    //if any of the exposed inputs where spent, remove them from our state
                    relevantState.PruneRecordsOfUsedInputs(txEvent.NewTransactionEvent.TransactionData.Transaction
                        .Inputs);
                }
            }));
            _ = BroadcastTransactionsPeriodically(cancellationToken);
            await loadCoins;
        }

        private async Task BroadcastTransactionsPeriodically(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tasks = new List<Task>();

                foreach (var state in _payJoinStateProvider.GetAll())
                {
                    var explorerClient = _explorerClientProvider.GetExplorerClient(state.Key.CryptoCode);
                    //broadcast any transaction sent to us that we have proposed a payjoin tx for but has not been broadcasted after x amount of time.
                    //This is imperative to preventing users from attempting to get as many utxos exposed from the merchant as possible.
                    var staleTxs = state.Value.GetStaleRecords(BroadcastAfter);

                    tasks.AddRange(staleTxs.Select(staleTx => explorerClient
                        .BroadcastAsync(staleTx.Transaction, cancellationToken)
                        .ContinueWith(task => { state.Value.RemoveRecord(staleTx, true); }, TaskScheduler.Default)));
                }

                await Task.WhenAll(tasks);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _payJoinStateProvider.SaveCoins();
            leases.Dispose();
        }
    }
}
