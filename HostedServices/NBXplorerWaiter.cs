using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

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
            public BTCPayNetworkBase Network { get; set; }
            public NBXplorerState State { get; set; }
            public StatusResult Status { get; set; }
            public string Error { get; set; }
            public GetMempoolInfoResponse MempoolInfo { get; set; }
        }

        readonly ConcurrentDictionary<string, NBXplorerSummary> _Summaries = new ConcurrentDictionary<string, NBXplorerSummary>();
        public void Publish(BTCPayNetworkBase network, NBXplorerState state, StatusResult status, GetMempoolInfoResponse mempoolInfo, string error)
        {
            var summary = new NBXplorerSummary() { Network = network, State = state, Status = status, MempoolInfo = mempoolInfo, Error = error };
            _Summaries.AddOrUpdate(network.CryptoCode.ToUpperInvariant(), summary, (k, v) => summary);
        }

        public bool IsFullySynched()
        {
            return _Summaries.All(s => s.Value.Status?.IsFullySynched is true);
        }

        public bool IsFullySynched(string cryptoCode, out NBXplorerSummary summary)
        {
            return _Summaries.TryGetValue(cryptoCode.ToUpperInvariant(), out summary) &&
                   summary.Status?.IsFullySynched is true;
        }
        public NBXplorerSummary Get(string cryptoCode)
        {
            _Summaries.TryGetValue(cryptoCode.ToUpperInvariant(), out var summary);
            return summary;
        }
        public IEnumerable<NBXplorerSummary> GetAll()
        {
            return _Summaries.Values;
        }
    }

    public class NBXplorerWaiters : IHostedService
    {
        readonly List<NBXplorerWaiter> _Waiters = new List<NBXplorerWaiter>();
        public NBXplorerWaiters(NBXplorerDashboard dashboard, ExplorerClientProvider explorerClientProvider, EventAggregator eventAggregator, Logs logs)
        {
            foreach (var explorer in explorerClientProvider.GetAll())
            {
                _Waiters.Add(new NBXplorerWaiter(dashboard, explorer.Item1, explorer.Item2, eventAggregator, logs));
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

        public NBXplorerWaiter(NBXplorerDashboard dashboard, BTCPayNetwork network, ExplorerClient client, EventAggregator aggregator, Logs logs)
        {
            this.Logs = logs;
            _Network = network;
            _Client = client;
            _Aggregator = aggregator;
            _Dashboard = dashboard;
            _Dashboard.Publish(_Network, State, null, null, null);
        }

        readonly NBXplorerDashboard _Dashboard;

        public Logs Logs { get; }

        readonly BTCPayNetwork _Network;
        readonly EventAggregator _Aggregator;
        readonly ExplorerClient _Client;

        CancellationTokenSource _Cts;
        Task _Loop;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Loop = StartLoop(_Cts.Token);
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation($"Starting listening NBXplorer ({_Network.CryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        while (await StepAsync(cancellation))
                        {

                        }
                        await Task.Delay(PollInterval, cancellation);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in NBXplorerWaiter ({_Network.CryptoCode})");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }

        private async Task<bool> StepAsync(CancellationToken cancellation)
        {
            var oldState = State;
            string error = null;
            StatusResult status = null;
            var mempoolInfoAsync = this._Client.RPCClient.GetMempoolInfo(cancellation);
            try
            {
                switch (State)
                {
                    case NBXplorerState.NotConnected:
                        status = await _Client.GetStatusAsync(cancellation);
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
                        status = await _Client.GetStatusAsync(cancellation);
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
                        status = await _Client.GetStatusAsync(cancellation);
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

            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                error = ex.Message;
            }


            if (status == null && error == null)
                error = $"{_Network.CryptoCode}: NBXplorer does not support this cryptocurrency";

            if (status != null && error == null)
            {
                if (status.NetworkType != _Network.NBitcoinNetwork.ChainName)
                    error = $"{_Network.CryptoCode}: NBXplorer is on a different ChainType (actual: {status.NetworkType}, expected: {_Network.NBitcoinNetwork.ChainName})";
            }

            if (error != null)
            {
                State = NBXplorerState.NotConnected;
                status = null;
                Logs.PayServer.LogError($"{_Network.CryptoCode}: NBXplorer error `{error}`");
            }

            GetMempoolInfoResponse mempoolInfo = null;
            try
            {
                mempoolInfo = await mempoolInfoAsync;
            }
            catch { }
            _Dashboard.Publish(_Network, State, status, mempoolInfo, error);
            if (oldState != State)
            {
                if (State == NBXplorerState.Synching)
                {
                    PollInterval = TimeSpan.FromSeconds(10);
                }
                else
                {
                    PollInterval = TimeSpan.FromMinutes(1);
                }
                _Aggregator.Publish(new NBXplorerStateChangedEvent(_Network, oldState, State));
            }
            return oldState != State;
        }

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1.0);

        public NBXplorerState State { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts.Cancel();
            return _Loop;
        }
    }
}
