using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.HostedServices
{
    public class EventHostedServiceBase : IHostedService
    {
        private readonly EventAggregator _EventAggregator;

        private List<IEventAggregatorSubscription> _Subscriptions;
        private CancellationTokenSource _Cts;

        public EventHostedServiceBase(EventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }

        Channel<object> _Events = Channel.CreateUnbounded<object>();
        public async Task ProcessEvents(CancellationToken cancellationToken)
        {
            while (await _Events.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_Events.Reader.TryRead(out var evt))
                {
                    try
                    {
                        await ProcessEvent(evt, cancellationToken);
                    }
                    catch when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogWarning(ex, $"Unhandled exception in {this.GetType().Name}");
                    }
                }
            }
        }

        protected virtual Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }


        protected virtual void SubscibeToEvents()
        {

        }

        protected void Subscribe<T>()
        {
            _Subscriptions.Add(_EventAggregator.Subscribe<T>(e => _Events.Writer.TryWrite(e)));
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _Subscriptions = new List<IEventAggregatorSubscription>();
            SubscibeToEvents();
            _Cts = new CancellationTokenSource();
            _ProcessingEvents = ProcessEvents(_Cts.Token);
            return Task.CompletedTask;
        }
        Task _ProcessingEvents = Task.CompletedTask;

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            _Subscriptions?.ForEach(subscription => subscription.Dispose());
            _Cts?.Cancel();
            try
            {
                await _ProcessingEvents;
            }
            catch (OperationCanceledException)
            { }
        }
    }
}
