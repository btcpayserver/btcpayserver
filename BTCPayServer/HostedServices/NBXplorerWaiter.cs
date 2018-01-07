using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using NBXplorer.Models;
using System.Collections.Concurrent;
using BTCPayServer.Events;

namespace BTCPayServer.HostedServices
{
    public enum NBXplorerState
    {
        NotConnected,
        Synching,
        Ready
    }

    public class NBXplorerDashboard
    {
        public class NBXplorerSummary
        {
            public BTCPayNetwork Network { get; set; }
            public NBXplorerState State { get; set; }
            public StatusResult Status { get; set; }
        }
        ConcurrentDictionary<string, NBXplorerSummary> _Summaries = new ConcurrentDictionary<string, NBXplorerSummary>();
        public void Publish(BTCPayNetwork network, NBXplorerState state, StatusResult status)
        {
            var summary = new NBXplorerSummary() { Network = network, State = state, Status = status };
            _Summaries.AddOrUpdate(network.CryptoCode, summary, (k, v) => summary);
        }

        public bool IsFullySynched()
        {
            return _Summaries.All(s => s.Value.Status != null && s.Value.Status.IsFullySynched);
        }

        public IEnumerable<NBXplorerSummary> GetAll()
        {
            return _Summaries.Values;
        }
    }

    public class NBXplorerWaiters : IHostedService
    {
        List<NBXplorerWaiter> _Waiters = new List<NBXplorerWaiter>();
        public NBXplorerWaiters(NBXplorerDashboard dashboard, ExplorerClientProvider explorerClientProvider, EventAggregator eventAggregator)
        {
            foreach (var explorer in explorerClientProvider.GetAll())
            {
                _Waiters.Add(new NBXplorerWaiter(dashboard, explorer.Item1, explorer.Item2, eventAggregator));
            }
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(_Waiters.Select(w => w.StartAsync(cancellationToken)).ToArray());
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(_Waiters.Select(w => w.StopAsync(cancellationToken)).ToArray());
        }
    }

    public class NBXplorerWaiter : IHostedService
    {

        public NBXplorerWaiter(NBXplorerDashboard dashboard, BTCPayNetwork network, ExplorerClient client, EventAggregator aggregator)
        {
            _Network = network;
            _Client = client;
            _Aggregator = aggregator;
            _Dashboard = dashboard;
        }

        NBXplorerDashboard _Dashboard;
        BTCPayNetwork _Network;
        EventAggregator _Aggregator;
        ExplorerClient _Client;
        Timer _Timer;
        ManualResetEventSlim _Idle = new ManualResetEventSlim(true);
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Timer = new Timer(Callback, null, 0, (int)TimeSpan.FromMinutes(1.0).TotalMilliseconds);
            return Task.CompletedTask;
        }

        void Callback(object state)
        {
            if (!_Idle.IsSet)
                return;
            try
            {
                _Idle.Reset();
                CheckStatus().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, "Error while checking NBXplorer state");
            }
            finally
            {
                _Idle.Set();
            }
        }

        async Task CheckStatus()
        {
            while (await StepAsync())
            {

            }
        }

        private async Task<bool> StepAsync()
        {
            var oldState = State;

            StatusResult status = null;
            switch (State)
            {
                case NBXplorerState.NotConnected:
                    status = await GetStatusWithTimeout();
                    if (status != null)
                    {
                        if (status.IsFullySynched)
                        {
                            State = NBXplorerState.Ready;
                        }
                        else
                        {
                            State = NBXplorerState.Synching;
                        }
                    }
                    break;
                case NBXplorerState.Synching:
                    status = await GetStatusWithTimeout();
                    if (status == null)
                    {
                        State = NBXplorerState.NotConnected;
                    }
                    else if (status.IsFullySynched)
                    {
                        State = NBXplorerState.Ready;
                    }
                    break;
                case NBXplorerState.Ready:
                    status = await GetStatusWithTimeout();
                    if (status == null)
                    {
                        State = NBXplorerState.NotConnected;
                    }
                    else if (!status.IsFullySynched)
                    {
                        State = NBXplorerState.Synching;
                    }
                    break;
            }

            if (oldState != State)
            {
                if (State == NBXplorerState.Synching)
                {
                    SetInterval(TimeSpan.FromSeconds(10));
                }
                else
                {
                    SetInterval(TimeSpan.FromMinutes(1));
                }
                _Aggregator.Publish(new NBXplorerStateChangedEvent(_Network, oldState, State));
            }
            _Dashboard.Publish(_Network, State, status);
            return oldState != State;
        }

        private void SetInterval(TimeSpan interval)
        {
            try
            {
                _Timer.Change(0, (int)interval.TotalMilliseconds);
            }
            catch { }
        }

        private async Task<StatusResult> GetStatusWithTimeout()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            using (cts)
            {
                var cancellation = cts.Token;
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        var status = await _Client.GetStatusAsync(cancellation).ConfigureAwait(false);
                        return status;
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }
            }
            return null;
        }

        public NBXplorerState State { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Timer.Dispose();
            _Timer = null;
            _Idle.Wait();
            return Task.CompletedTask;
        }
    }
}
