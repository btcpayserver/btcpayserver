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

namespace BTCPayServer
{
    public class NBXplorerWaiterAccessor
    {
        public NBXplorerWaiter Instance { get; set; }
    }
    public enum NBXplorerState
    {
        NotConnected,
        Synching,
        Ready
    }

    public class NBXplorerWaiter : IHostedService
    {
        public NBXplorerWaiter(ExplorerClient client, EventAggregator aggregator, NBXplorerWaiterAccessor accessor)
        {
            _Client = client;
            _Aggregator = aggregator;
            accessor.Instance = this;
        }

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
                        if (status.IsFullySynched())
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
                    else if (status.IsFullySynched())
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
                    else if (!status.IsFullySynched())
                    {
                        State = NBXplorerState.Synching;
                    }
                    break;
            }

            LastStatus = status;
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
                _Aggregator.Publish(new NBXplorerStateChangedEvent(oldState, State));
            }
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

        public StatusResult LastStatus { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Timer.Dispose();
            _Timer = null;
            _Idle.Wait();
            return Task.CompletedTask;
        }
    }
}
