#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices
{
    public class EventHostedServiceBase : IHostedService
    {
        private readonly EventAggregator _EventAggregator;

        public Logs Logs { get; }

        public EventAggregator EventAggregator => _EventAggregator;

        private List<IEventAggregatorSubscription> _Subscriptions = new List<IEventAggregatorSubscription>();
        private CancellationTokenSource _Cts = new CancellationTokenSource();
        public CancellationToken CancellationToken => _Cts.Token;
        public EventHostedServiceBase(EventAggregator eventAggregator, Logs logs)
        {
            _EventAggregator = eventAggregator;
            Logs = logs;
        }

        public EventHostedServiceBase(EventAggregator eventAggregator, ILogger logger)
        {
            _EventAggregator = eventAggregator;
            Logs = new Logs() { PayServer = logger, Events = logger, Configuration = logger };
        }

        readonly Channel<object> _Events = Channel.CreateUnbounded<object>();
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


        protected virtual void SubscribeToEvents()
        {

        }

        protected void Subscribe<T>()
        {
            _Subscriptions.Add(_EventAggregator.Subscribe<T>(e => _Events.Writer.TryWrite(e!)));
        }

        protected void PushEvent(object obj)
        {
            _Events.Writer.TryWrite(obj);
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            SubscribeToEvents();
            _ProcessingEvents = ProcessEvents(_Cts.Token);
            return Task.CompletedTask;
        }
        Task _ProcessingEvents = Task.CompletedTask;

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            _Subscriptions.ForEach(subscription => subscription.Dispose());
            _Cts.Cancel();
            try
            {
                await _ProcessingEvents;
            }
            catch (OperationCanceledException)
            { }
        }
    }
}
