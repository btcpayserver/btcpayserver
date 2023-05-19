using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;

namespace BTCPayServer
{
    public interface IEventAggregatorSubscription : IDisposable
    {
        void Unsubscribe();
    }
    public class EventAggregator : IDisposable
    {
        public EventAggregator(Logs logs)
        {
            Logs = logs;
        }
        class Subscription : IEventAggregatorSubscription
        {
            private readonly EventAggregator aggregator;
            readonly Type t;
            public Subscription(EventAggregator aggregator, Type t)
            {
                this.aggregator = aggregator;
                this.t = t;
            }

            public Action<Object> Act { get; set; }

            bool _Disposed;
            public void Dispose()
            {
                if (_Disposed)
                    return;
                _Disposed = true;
                lock (this.aggregator._Subscriptions)
                {
                    if (this.aggregator._Subscriptions.TryGetValue(t, out Dictionary<Subscription, Action<object>> actions))
                    {
                        if (actions.Remove(this))
                        {
                            if (actions.Count == 0)
                                this.aggregator._Subscriptions.Remove(t);
                        }
                    }
                }
            }

            public void Unsubscribe()
            {
                Dispose();
            }
        }
        public Task<T> WaitNext<T>(CancellationToken cancellation = default(CancellationToken))
        {
            return WaitNext<T>(o => true, cancellation);
        }
        public async Task<T> WaitNext<T>(Func<T, bool> predicate, CancellationToken cancellation = default(CancellationToken))
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            using var subscription = Subscribe<T>((a, b) => { if (predicate(b)) { tcs.TrySetResult(b); a.Unsubscribe(); } });
            using (cancellation.Register(() => { tcs.TrySetCanceled(); }))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public void Publish<T>(T evt) where T : class
        {
            Publish(evt, typeof(T));
        }

        public void Publish(object evt, Type evtType)
        {
            ArgumentNullException.ThrowIfNull(evt);
            List<Action<object>> actionList = new List<Action<object>>();
            lock (_Subscriptions)
            {
                if (_Subscriptions.TryGetValue(evtType, out Dictionary<Subscription, Action<object>> actions))
                {
                    actionList = actions.Values.ToList();
                }
            }

            foreach (var sub in actionList)
            {
                try
                {
                    sub(evt);
                }
                catch (Exception ex)
                {
                    Logs.Events.LogError(ex, $"Error while calling event handler");
                }
            }
        }

        public IEventAggregatorSubscription Subscribe<T>(Action<IEventAggregatorSubscription, T> subscription)
        {
            var eventType = typeof(T);
            var s = new Subscription(this, eventType);
            s.Act = (o) => subscription(s, (T)o);
            return Subscribe(eventType, s);
        }

        public IEventAggregatorSubscription Subscribe(Type eventType, Action<IEventAggregatorSubscription, object> subscription)
        {
            var s = new Subscription(this, eventType);
            s.Act = (o) => subscription(s, o);
            return Subscribe(eventType, s);
        }

        private IEventAggregatorSubscription Subscribe(Type eventType, Subscription subscription)
        {
            lock (_Subscriptions)
            {
                if (!_Subscriptions.TryGetValue(eventType, out Dictionary<Subscription, Action<object>> actions))
                {
                    actions = new Dictionary<Subscription, Action<object>>();
                    _Subscriptions.Add(eventType, actions);
                }
                actions.Add(subscription, subscription.Act);
            }
            return subscription;
        }

        readonly Dictionary<Type, Dictionary<Subscription, Action<object>>> _Subscriptions = new Dictionary<Type, Dictionary<Subscription, Action<object>>>();

        public Logs Logs { get; }

        public IEventAggregatorSubscription Subscribe<T, TReturn>(Func<T, TReturn> subscription)
        {
            return Subscribe(new Action<T>((t) => subscription(t)));
        }

        public IEventAggregatorSubscription Subscribe<T, TReturn>(Func<IEventAggregatorSubscription, T, TReturn> subscription)
        {
            return Subscribe(new Action<IEventAggregatorSubscription, T>((sub, t) => subscription(sub, t)));
        }
        class ChannelSubscription<T> : IEventAggregatorSubscription
        {
            private Channel<T> _evts;
            private IEventAggregatorSubscription _innerSubscription;
            private Func<T, Task> _act;
            private Logs _logs;

            public ChannelSubscription(Channel<T> evts, IEventAggregatorSubscription innerSubscription, Func<T, Task> act, Logs logs)
            {
                _evts = evts;
                _innerSubscription = innerSubscription;
                _act = act;
                _logs = logs;
                _ = Listen();
            }

            private async Task Listen()
            {
                await foreach (var item in _evts.Reader.ReadAllAsync())
                {
                    try
                    {
                        await _act(item);
                    }
                    catch (Exception ex)
                    {
                        _logs.Events.LogError(ex, $"Error while calling event async handler");
                    }
                }
            }

            public void Dispose()
            {
                Unsubscribe();
            }

            public void Unsubscribe()
            {
                _innerSubscription.Unsubscribe();
                _evts.Writer.TryComplete();
            }
        }
        public IEventAggregatorSubscription SubscribeAsync<T>(Func<T, Task> subscription)
        {
            Channel<T> evts = Channel.CreateUnbounded<T>();
            var innerSubscription = Subscribe(new Action<IEventAggregatorSubscription, T>((sub, t) => evts.Writer.TryWrite(t)));
            return new ChannelSubscription<T>(evts, innerSubscription, subscription, Logs);
        }
        public IEventAggregatorSubscription Subscribe<T>(Action<T> subscription)
        {
            return Subscribe(new Action<IEventAggregatorSubscription, T>((sub, t) => subscription(t)));
        }

        public void Dispose()
        {
            lock (_Subscriptions)
            {
                _Subscriptions.Clear();
            }
        }
    }
}
