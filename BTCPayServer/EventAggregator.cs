using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;

namespace BTCPayServer
{
    public interface IEventAggregatorSubscription : IDisposable
    {
        void Unsubscribe();
        void Resubscribe();
    }
    public class EventAggregator : IDisposable
    {
        class Subscription : IEventAggregatorSubscription
        {
            private EventAggregator aggregator;
            Type t;
            Action<object> act;
            public Subscription(EventAggregator aggregator, Type t)
            {
                this.aggregator = aggregator;
                this.t = t;
            }

            public Action<Object> Act { get; set; }

            public void Dispose()
            {
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

            public void Resubscribe()
            {
                aggregator.Subscribe(t, this);
            }

            public void Unsubscribe()
            {
                Dispose();
            }
        }

        public void Publish<T>(T evt) where T : class
        {
            if (evt == null)
                throw new ArgumentNullException(nameof(evt));
            List<Action<object>> actionList = new List<Action<object>>();
            lock (_Subscriptions)
            {
                if (_Subscriptions.TryGetValue(typeof(T), out Dictionary<Subscription, Action<object>> actions))
                {
                    actionList = actions.Values.ToList();
                }
            }

            Logs.Events.LogInformation($"New event: {evt.ToString()}");
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

        Dictionary<Type, Dictionary<Subscription, Action<object>>> _Subscriptions = new Dictionary<Type, Dictionary<Subscription, Action<object>>>();

        public IEventAggregatorSubscription Subscribe<T, TReturn>(Func<T, TReturn> subscription)
        {
            return Subscribe(new Action<T>((t) => subscription(t)));
        }

        public IEventAggregatorSubscription Subscribe<T, TReturn>(Func<IEventAggregatorSubscription, T, TReturn> subscription)
        {
            return Subscribe(new Action<IEventAggregatorSubscription, T>((sub, t) => subscription(sub, t)));
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
