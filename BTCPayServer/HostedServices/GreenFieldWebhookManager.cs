using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Events;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.HostedServices
{
    public interface IGreenFieldWebhookScopeKeyFetcher
    {
        bool CanFetch(WebhookScope webhookScope);

        Task<Key> Fetch(WebhookScope webhookScope);
    }

    public abstract class GreenFieldWebhookScopeKeyFetcher<T> : IGreenFieldWebhookScopeKeyFetcher where T : WebhookScope
    {
        public virtual bool CanFetch(WebhookScope webhookScope)
        {
            return webhookScope is T;
        }

        public abstract Task<Key> Fetch(T webhookScope);
        
        public Task<Key> Fetch(WebhookScope webhookScope)
        {
            return Fetch((T)webhookScope);
        }
    }
    
    
    public interface WebhookScope
    {
            
    }

    public class InvoiceWebhookScope : WebhookScope
    {
        public string InvoiceId { get; set; }
        public string StoreId { get; set; }
    }
    
    public class InvoiceGreenFieldWebhookScopeKeyFetcher: GreenFieldWebhookScopeKeyFetcher<InvoiceWebhookScope>
    {
        private readonly StoreRepository _storeRepository;

        public InvoiceGreenFieldWebhookScopeKeyFetcher(StoreRepository storeRepository)
        {
            _storeRepository = storeRepository;
        }
        public override async Task<Key> Fetch(InvoiceWebhookScope webhookScope)
        {
            var store = await _storeRepository.FindStore(webhookScope.StoreId);
            return store?.GetStoreBlob()?.EventSigner;
        }
    }


    public class GreenFieldWebhookVerificationKeyFetcher
    {
        private IEnumerable<IGreenFieldWebhookScopeKeyFetcher> _keyFetchers;

        public GreenFieldWebhookVerificationKeyFetcher(IEnumerable<IGreenFieldWebhookScopeKeyFetcher> keyFetchers)
        {
            _keyFetchers = keyFetchers;
        }
        Task<Key> GetKey(WebhookScope webhookScope)
        {
            return _keyFetchers.FirstOrDefault(fetcher => fetcher.CanFetch(webhookScope))?.Fetch(webhookScope) ??
                   Task.FromResult<Key>(null);
        }
    }
    
    public class GreenFieldWebhookManager : EventHostedServiceBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly GreenFieldWebhookVerificationKeyFetcher _greenFieldWebhookVerificationKeyFetcher;
        private readonly EventAggregator _eventAggregator;

        public GreenFieldWebhookManager(EventAggregator eventAggregator, IHttpClientFactory httpClientFactory,
            IBackgroundJobClient backgroundJobClient, GreenFieldWebhookVerificationKeyFetcher greenFieldWebhookVerificationKeyFetcher) : base(
            eventAggregator)
        {
            _httpClientFactory = httpClientFactory;
            _backgroundJobClient = backgroundJobClient;
            _greenFieldWebhookVerificationKeyFetcher = greenFieldWebhookVerificationKeyFetcher;
            _eventAggregator = eventAggregator;
        }

        public const string OnionNamedClient = "greenfield-webhook.onion";
        public const string ClearnetNamedClient = "greenfield-webhook.clearnet";

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Subscribe<InvoiceDataChangedEvent>();
            Subscribe<QueuedGreenFieldWebHook>();
        }

        private HttpClient GetClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion() ? OnionNamedClient : ClearnetNamedClient);
        }

        readonly Dictionary<string, Task> _queuedSendingTasks = new Dictionary<string, Task>();


        /// <summary>
        /// Will make sure only one callback is called at once on the same key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="sendRequest"></param>
        /// <returns></returns>
        private async Task Enqueue(string key, Func<Task> sendRequest)
        {
            Task sending = null;
            lock (_queuedSendingTasks)
            {
                if (_queuedSendingTasks.TryGetValue(key, out var executing))
                {
                    var completion = new TaskCompletionSource<bool>();
                    sending = completion.Task;
                    _queuedSendingTasks.Remove(key);
                    _queuedSendingTasks.Add(key, sending);
                    _ = executing.ContinueWith(_ =>
                    {
                        sendRequest()
                            .ContinueWith(t =>
                            {
                                if (t.Status == TaskStatus.RanToCompletion)
                                {
                                    completion.TrySetResult(true);
                                }

                                if (t.Status == TaskStatus.Faulted)
                                {
                                    completion.TrySetException(t.Exception);
                                }

                                if (t.Status == TaskStatus.Canceled)
                                {
                                    completion.TrySetCanceled();
                                }
                            }, TaskScheduler.Default);
                    }, TaskScheduler.Default);
                }
                else
                {
                    sending = sendRequest();
                    _queuedSendingTasks.Add(key, sending);
                }

                _ = sending.ContinueWith(o =>
                {
                    lock (_queuedSendingTasks)
                    {
                        _queuedSendingTasks.TryGetValue(key, out var executing2);
                        if (executing2 == sending)
                            _queuedSendingTasks.Remove(key);
                    }
                }, TaskScheduler.Default);
            }

            await sending;
        }

        protected override Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case InvoiceDataChangedEvent e:
                    var validWebhooks = e.Invoice.Webhooks.Where(subscription =>
                        ValidWebhookForEvent(subscription, InvoiceStatusChangeEventPayload.EventType)).ToList();
                    if (validWebhooks.Any())
                    {
                        var payload = new InvoiceStatusChangeEventPayload()
                        {
                            Status = e.State.Status,
                            AdditionalStatus = e.State.ExceptionStatus,
                            InvoiceId = e.InvoiceId
                        };
                        var webHook = new GreenFieldEvent<InvoiceStatusChangeEventPayload>()
                        {
                            EventType = InvoiceStatusChangeEventPayload.EventType, Payload = payload
                        };
                        foreach (WebhookSubscription webhookSubscription in validWebhooks)
                        {
                            _eventAggregator.Publish(new QueuedGreenFieldWebHook()
                            {
                                Event = webHook,
                                Subscription = webhookSubscription,
                                Grouping = webhookSubscription.ToString(),
                                Scope = new InvoiceWebhookScope()
                                {
                                    InvoiceId = e.Invoice.Id,
                                    StoreId = e.Invoice.StoreId
                                },
                            });
                        }
                    }

                    break;
                case QueuedGreenFieldWebHook e:
                    _ = Enqueue(e.Grouping, () => Send(e, cancellationToken));
                    break;
            }

            return base.ProcessEvent(evt, cancellationToken);
        }

        private async Task Send(QueuedGreenFieldWebHook e, CancellationToken cancellationToken)
        {
            var client = GetClient(e.Subscription.Url);
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = e.Subscription.Url,
                Content = new StringContent(JsonConvert.SerializeObject(e.Event),
                    Encoding.UTF8, "application/json")
            };
            var webhookEventResultError = "";
            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(1.0));
                var result = await client.SendAsync(request, cts.Token);
                if (result.IsSuccessStatusCode)
                {
                    _eventAggregator.Publish(new GreenFieldWebhookResultEvent()
                    {
                        Error = webhookEventResultError, Hook = e
                    });

                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // When the JobClient will be persistent, this will reschedule the job for after reboot
                _backgroundJobClient.Schedule((cancellation) => Send(e, cancellation), TimeSpan.FromMinutes(10.0));
                return;
            }
            catch (OperationCanceledException)
            {
                webhookEventResultError = "Timeout";
            }
            catch (Exception ex)
            {
                List<string> messages = new List<string>();
                while (ex != null)
                {
                    messages.Add(ex.Message);
                    ex = ex.InnerException;
                }

                string message = string.Join(',', messages.ToArray());

                webhookEventResultError = $"Unexpected error: {message}";
            }

            e.TryCount++;
            _eventAggregator.Publish(new GreenFieldWebhookResultEvent()
            {
                Error = webhookEventResultError, Hook = e
            });

            if (e.TryCount <= QueuedGreenFieldWebHook.MaxTry)
                _backgroundJobClient.Schedule((cancellation) => Send(e, cancellation), TimeSpan.FromMinutes(10.0));
        }

        public bool ValidWebhookForEvent(WebhookSubscription webhookSubscription, string eventType)
        {
            return eventType.StartsWith(webhookSubscription.EventType, StringComparison.InvariantCultureIgnoreCase);
        }

        public class QueuedGreenFieldWebHook
        {
            public string Grouping { get; set; }
            public WebhookSubscription Subscription { get; set; }
            public IGreenFieldEvent Event { get; set; }
            public int TryCount { get; set; } = 0;
            public const int MaxTry = 6;
            public WebhookScope Scope { get; set; }
        }
    }
}
