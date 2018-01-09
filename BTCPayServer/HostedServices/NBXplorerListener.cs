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
        NBXplorerDashboard _Dashboards;

        public NBXplorerListener(ExplorerClientProvider explorerClients,
                                NBXplorerDashboard dashboard,
                                InvoiceRepository invoiceRepository,
                                EventAggregator aggregator, IApplicationLifetime lifetime)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _Dashboards = dashboard;
            _InvoiceRepository = invoiceRepository;
            _ExplorerClients = explorerClients;
            _Aggregator = aggregator;
            _Lifetime = lifetime;
        }

        CompositeDisposable leases = new CompositeDisposable();
        ConcurrentDictionary<string, NotificationSession> _Sessions = new ConcurrentDictionary<string, NotificationSession>();
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
                    await Listen(nbxplorerEvent.Network);
                }
            }));

            _ListenPoller = new Timer(async s =>
            {
                foreach (var nbxplorerState in _Dashboards.GetAll())
                {
                    if (nbxplorerState.Status != null && nbxplorerState.Status.IsFullySynched)
                    {
                        await Listen(nbxplorerState.Network);
                    }
                }
            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);

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

        private async Task Listen(BTCPayNetwork network)
        {
            bool cleanup = false;
            try
            {
                if (_Sessions.ContainsKey(network.CryptoCode))
                    return;
                var client = _ExplorerClients.GetExplorerClient(network);
                if (client == null)
                    return;
                if (_Cts.IsCancellationRequested)
                    return;
                var session = await client.CreateNotificationSessionAsync(_Cts.Token).ConfigureAwait(false);
                if (!_Sessions.TryAdd(network.CryptoCode, session))
                {
                    await session.DisposeAsync();
                    return;
                }
                cleanup = true;
                using (session)
                {
                    await session.ListenNewBlockAsync(_Cts.Token).ConfigureAwait(false);
                    await session.ListenDerivationSchemesAsync((await GetStrategies(network)).ToArray(), _Cts.Token).ConfigureAwait(false);
                    Logs.PayServer.LogInformation($"Connected to WebSocket of NBXplorer ({network.CryptoCode})");
                    while (!_Cts.IsCancellationRequested)
                    {
                        var newEvent = await session.NextEventAsync(_Cts.Token).ConfigureAwait(false);
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
                                        Network = network,
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
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, $"Error while connecting to WebSocket of NBXplorer ({network.CryptoCode})");
            }
            finally
            {
                if (cleanup)
                {
                    Logs.PayServer.LogInformation($"Disconnected from WebSocket of NBXplorer ({network.CryptoCode})");
                    _Sessions.TryRemove(network.CryptoCode, out NotificationSession unused);
                    if (_Sessions.Count == 0 && _Cts.IsCancellationRequested)
                    {
                        _RunningTask.TrySetResult(true);
                    }
                }
            }
        }

        private async Task<List<DerivationStrategyBase>> GetStrategies(BTCPayNetwork network)
        {
            List<DerivationStrategyBase> strategies = new List<DerivationStrategyBase>();
            foreach (var invoiceId in await _InvoiceRepository.GetPendingInvoices())
            {
                var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
                var strategy = GetStrategy(network.CryptoCode, invoice);
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
