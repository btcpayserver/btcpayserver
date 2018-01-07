using System;
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
using NBXplorer.DerivationStrategy;
using BTCPayServer.Events;

namespace BTCPayServer.HostedServices
{
    public class NBXplorerListener : IHostedService
    {
        EventAggregator _Aggregator;
        ExplorerClientProvider _ExplorerClients;
        IApplicationLifetime _Lifetime;
        InvoiceRepository _InvoiceRepository;
        private TaskCompletionSource<bool> _RunningTask;
        private CancellationTokenSource _Cts;

        public NBXplorerListener(ExplorerClientProvider explorerClients,
                                InvoiceRepository invoiceRepository,
                                EventAggregator aggregator, IApplicationLifetime lifetime)
        {
            _InvoiceRepository = invoiceRepository;
            _ExplorerClients = explorerClients;
            _Aggregator = aggregator;
            _Lifetime = lifetime;
        }

        CompositeDisposable leases = new CompositeDisposable();
        ConcurrentDictionary<string, NotificationSession> _Sessions = new ConcurrentDictionary<string, NotificationSession>();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _RunningTask = new TaskCompletionSource<bool>();
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leases.Add(_Aggregator.Subscribe<Events.NBXplorerStateChangedEvent>(async nbxplorerEvent =>
            {
                if (nbxplorerEvent.NewState == NBXplorerState.Ready)
                {
                    if (_Sessions.ContainsKey(nbxplorerEvent.Network.CryptoCode))
                        return;
                    var client = _ExplorerClients.GetExplorerClient(nbxplorerEvent.Network);
                    var session = await client.CreateNotificationSessionAsync(_Cts.Token);
                    if (!_Sessions.TryAdd(nbxplorerEvent.Network.CryptoCode, session))
                    {
                        await session.DisposeAsync();
                        return;
                    }

                    try
                    {
                        using (session)
                        {
                            await session.ListenNewBlockAsync(_Cts.Token);
                            await session.ListenDerivationSchemesAsync((await GetStrategies(nbxplorerEvent)).ToArray(), _Cts.Token);
                            Logs.PayServer.LogInformation($"Start Listening {nbxplorerEvent.Network.CryptoCode} explorer events");
                            while (true)
                            {
                                var newEvent = await session.NextEventAsync(_Cts.Token);
                                switch (newEvent)
                                {
                                    case NBXplorer.Models.NewBlockEvent evt:
                                        _Aggregator.Publish(new Events.NewBlockEvent());
                                        break;
                                    case NBXplorer.Models.NewTransactionEvent evt:
                                        foreach (var txout in evt.Match.Outputs)
                                        {
                                            _Aggregator.Publish(new Events.TxOutReceivedEvent()
                                            {
                                                Network = nbxplorerEvent.Network,
                                                ScriptPubKey = txout.ScriptPubKey
                                            });
                                        }
                                        break;
                                    default:
                                        Logs.PayServer.LogWarning("Received unknown message from NBXplorer");
                                        break;
                                }
                            }
                        }
                    }
                    catch when (_Cts.IsCancellationRequested) { }
                    finally
                    {
                        Logs.PayServer.LogInformation($"Stop listening {nbxplorerEvent.Network.CryptoCode} explorer events");
                        _Sessions.TryRemove(nbxplorerEvent.Network.CryptoCode, out NotificationSession unused);
                        if(_Sessions.Count == 0 && _Cts.IsCancellationRequested)
                        {
                            _RunningTask.TrySetResult(true);
                        }
                    }
                }
            }));

            leases.Add(_Aggregator.Subscribe<Events.InvoiceCreatedEvent>(async inv =>
            {
                var invoice = await _InvoiceRepository.GetInvoice(null, inv.InvoiceId);
                List<Task> listeningDerivations = new List<Task>();
                foreach (var notificationSessions in _Sessions)
                {
                    var derivationStrategy = GetStrategy(notificationSessions.Key, invoice);
                    if (derivationStrategy != null)
                    {
                        listeningDerivations.Add(notificationSessions.Value.ListenDerivationSchemesAsync(new[] { derivationStrategy }, _Cts.Token));
                    }
                }
                await Task.WhenAll(listeningDerivations.ToArray()).ConfigureAwait(false);
            }));
            return Task.CompletedTask;
        }

        private async Task<List<DerivationStrategyBase>> GetStrategies(NBXplorerStateChangedEvent nbxplorerEvent)
        {
            List<DerivationStrategyBase> strategies = new List<DerivationStrategyBase>();
            foreach (var invoiceId in await _InvoiceRepository.GetPendingInvoices())
            {
                var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
                var strategy = GetStrategy(nbxplorerEvent.Network.CryptoCode, invoice);
                if (strategy != null)
                    strategies.Add(strategy);
            }

            return strategies;
        }

        private DerivationStrategyBase GetStrategy(string cryptoCode, InvoiceEntity invoice)
        {
            foreach (var derivationStrategy in invoice.GetDerivationStrategies(_ExplorerClients.NetworkProviders))
            {
                if (derivationStrategy.Network.CryptoCode == cryptoCode)
                {
                    return derivationStrategy.DerivationStrategyBase;
                }
            }
            return null;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            return Task.WhenAny(_RunningTask.Task, Task.Delay(-1, cancellationToken));
        }
    }
}
