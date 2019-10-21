using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using BTCPayServer.Lightning;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http;
using BTCPayServer.Services;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningListener : IHostedService
    {
        EventAggregator _Aggregator;
        InvoiceRepository _InvoiceRepository;
        private readonly IMemoryCache _memoryCache;
        BTCPayNetworkProvider _NetworkProvider;
        private readonly LightningClientFactoryService lightningClientFactory;
        Channel<string> _CheckInvoices = Channel.CreateUnbounded<string>();
        Task _CheckingInvoice;
        Dictionary<(string, string), LightningInstanceListener> _InstanceListeners = new Dictionary<(string, string), LightningInstanceListener>();

        public LightningListener(EventAggregator aggregator,
                              InvoiceRepository invoiceRepository,
                              IMemoryCache memoryCache,
                              BTCPayNetworkProvider networkProvider,
                              LightningClientFactoryService lightningClientFactory)
        {
            _Aggregator = aggregator;
            _InvoiceRepository = invoiceRepository;
            _memoryCache = memoryCache;
            _NetworkProvider = networkProvider;
            this.lightningClientFactory = lightningClientFactory;
        }

        async Task CheckingInvoice(CancellationToken cancellation)
        {
            while(await _CheckInvoices.Reader.WaitToReadAsync(cancellation) && 
                        _CheckInvoices.Reader.TryRead(out var invoiceId))
            {
                try
                {
                    foreach (var listenedInvoice in (await GetListenedInvoices(invoiceId)).Where(i => !i.IsExpired()))
                    {
                        var instanceListenerKey = (listenedInvoice.Network.CryptoCode, listenedInvoice.SupportedPaymentMethod.GetLightningUrl().ToString());
                        if (!_InstanceListeners.TryGetValue(instanceListenerKey, out var instanceListener) || 
                            !instanceListener.IsListening)
                        {
                            instanceListener = instanceListener ?? new LightningInstanceListener(_InvoiceRepository, _Aggregator, listenedInvoice.SupportedPaymentMethod, lightningClientFactory, listenedInvoice.Network);
                            var status = await instanceListener.PollPayment(listenedInvoice, cancellation);
                            if (status is null ||
                                status is LightningInvoiceStatus.Paid ||
                                status is LightningInvoiceStatus.Expired)
                            {
                                continue;
                            }
                            instanceListener.AddListenedInvoice(listenedInvoice);
                            instanceListener.EnsureListening(cancellation);
                            _InstanceListeners.TryAdd(instanceListenerKey, instanceListener);
                        }
                        else
                        {
                            instanceListener.AddListenedInvoice(listenedInvoice);
                        }
                    }
                    foreach (var kv in _InstanceListeners)
                    {
                        kv.Value.RemoveExpiredInvoices();
                    }
                    foreach (var k in _InstanceListeners
                                                    .Where(kv => !kv.Value.IsListening)
                                                    .Select(kv => kv.Key).ToArray())
                    {
                        _InstanceListeners.Remove(k);
                    }
                }
                catch when (!_Cts.Token.IsCancellationRequested)
                {

                }
            }
        }
        private Task<List<ListenedInvoice>> GetListenedInvoices(string invoiceId)
        {
            return _memoryCache.GetOrCreateAsync(invoiceId, async (cacheEntry) =>
            {
                var listenedInvoices = new List<ListenedInvoice>();
                var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
                foreach (var paymentMethod in invoice.GetPaymentMethods()
                                                              .Where(c => c.GetId().PaymentType == PaymentTypes.LightningLike))
                {
                    var lightningMethod = paymentMethod.GetPaymentMethodDetails() as LightningLikePaymentMethodDetails;
                    if (lightningMethod == null)
                        continue;
                    var lightningSupportedMethod = invoice.GetSupportedPaymentMethod<LightningSupportedPaymentMethod>()
                                              .FirstOrDefault(c => c.CryptoCode == paymentMethod.GetId().CryptoCode);
                    if (lightningSupportedMethod == null)
                        continue;
                    var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.GetId().CryptoCode);

                    listenedInvoices.Add(new ListenedInvoice()
                    {
                        Expiration = invoice.ExpirationTime,
                        Uri = lightningSupportedMethod.GetLightningUrl().BaseUri.AbsoluteUri,
                        PaymentMethodDetails = lightningMethod,
                        SupportedPaymentMethod = lightningSupportedMethod,
                        PaymentMethod = paymentMethod,
                        Network = network,
                        InvoiceId = invoice.Id
                    });
                }
                var expiredIn = DateTimeOffset.UtcNow - invoice.ExpirationTime;
                cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow + (expiredIn >= TimeSpan.FromMinutes(5.0) ? expiredIn : TimeSpan.FromMinutes(5.0));
                return listenedInvoices;
            });
        }

        ConcurrentDictionary<string, LightningInstanceListener> _ListeningInstances = new ConcurrentDictionary<string, LightningInstanceListener>();


        CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_Aggregator.Subscribe<Events.InvoiceEvent>(inv =>
            {
                if (inv.Name == InvoiceEvent.Created)
                {
                    _CheckInvoices.Writer.TryWrite(inv.Invoice.Id);
                }
            }));
            _CheckingInvoice = CheckingInvoice(_Cts.Token);
            _ListenPoller = new Timer(async s =>
            {
                try
                {
                    var invoiceIds = await _InvoiceRepository.GetPendingInvoices();
                    foreach (var invoiceId in invoiceIds)
                        _CheckInvoices.Writer.TryWrite(invoiceId);
                }
                catch { } // Never throw an unhandled exception on async void

            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        TimeSpan _PollInterval = TimeSpan.FromMinutes(1.0);
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

        CancellationTokenSource _Cts = new CancellationTokenSource();
        
        HashSet<string> _InvoiceIds = new HashSet<string>();
        private Timer _ListenPoller;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            try
            {
                await _CheckingInvoice;
            }
            catch (OperationCanceledException)
            {

            }
            try
            {
                await Task.WhenAll(_ListeningInstances.Select(c => c.Value.Listening).ToArray());
            }
            catch (OperationCanceledException)
            {

            }
            Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
        }
    }


    public class LightningInstanceListener
    {
        private LightningSupportedPaymentMethod supportedPaymentMethod;
        private readonly InvoiceRepository invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetwork network;
        private readonly LightningClientFactoryService _lightningClientFactory;

        public LightningInstanceListener(InvoiceRepository invoiceRepository,
                                        EventAggregator eventAggregator,
                                        LightningSupportedPaymentMethod supportedPaymentMethod,
                                        LightningClientFactoryService lightningClientFactory,
                                        BTCPayNetwork network)
        {
            this.supportedPaymentMethod = supportedPaymentMethod;
            this.invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            this.network = network;
            _lightningClientFactory = lightningClientFactory;
        }
        internal bool AddListenedInvoice(ListenedInvoice invoice)
        {
            return _ListenedInvoices.TryAdd(invoice.PaymentMethodDetails.InvoiceId, invoice);
        }

        internal async Task<LightningInvoiceStatus?> PollPayment(ListenedInvoice listenedInvoice, CancellationToken cancellation)
        {
            var client = _lightningClientFactory.Create(supportedPaymentMethod.GetLightningUrl(), network);
            LightningInvoice lightningInvoice = await client.GetInvoice(listenedInvoice.PaymentMethodDetails.InvoiceId);
            if (lightningInvoice?.Status is LightningInvoiceStatus.Paid && 
                await AddPayment(lightningInvoice, listenedInvoice.InvoiceId))
            {
                Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Payment detected via polling on {listenedInvoice.InvoiceId}");
            }
            return lightningInvoice?.Status;
        }

        public bool IsListening => Listening?.Status is TaskStatus.Running || Listening?.Status is TaskStatus.WaitingForActivation;
        public Task Listening { get; set; }
        public void EnsureListening(CancellationToken cancellation)
        {
            if (!IsListening)
            {
                StopListeningCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                Listening = Listen(StopListeningCancellationTokenSource.Token);
            }
        }
        public CancellationTokenSource StopListeningCancellationTokenSource;
        async Task Listen(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Start listening {supportedPaymentMethod.GetLightningUrl().BaseUri}");
            try
            {
                var lightningClient = _lightningClientFactory.Create(supportedPaymentMethod.GetLightningUrl(), network);
                using (var session = await lightningClient.Listen(cancellation))
                {
                    // Just in case the payment arrived after our last poll but before we listened.
                    await PollAllListenedInvoices(cancellation);
                    if (_ErrorAlreadyLogged)
                    {
                        Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Could reconnect successfully to {supportedPaymentMethod.GetLightningUrl().BaseUri}");
                    }
                    _ErrorAlreadyLogged = false;
                    while (!_ListenedInvoices.IsEmpty)
                    {
                        var notification = await session.WaitInvoice(cancellation);
                        if (!_ListenedInvoices.TryGetValue(notification.Id, out var listenedInvoice))
                            continue;
                        if (notification.Id == listenedInvoice.PaymentMethodDetails.InvoiceId &&
                            notification.BOLT11 == listenedInvoice.PaymentMethodDetails.BOLT11)
                        {
                            if (notification.Status == LightningInvoiceStatus.Paid &&
                                notification.PaidAt.HasValue && notification.Amount != null)
                            {
                                if (await AddPayment(notification, listenedInvoice.InvoiceId))
                                {
                                    Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Payment detected via notification ({listenedInvoice.InvoiceId})");
                                }
                                _ListenedInvoices.TryRemove(notification.Id, out var _);
                            }
                            else if (notification.Status == LightningInvoiceStatus.Expired)
                            {
                                _ListenedInvoices.TryRemove(notification.Id, out var _);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested && !_ErrorAlreadyLogged)
            {
                _ErrorAlreadyLogged = true;
                Logs.PayServer.LogError(ex, $"{supportedPaymentMethod.CryptoCode} (Lightning): Error while contacting {supportedPaymentMethod.GetLightningUrl().BaseUri}");
                Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Stop listening {supportedPaymentMethod.GetLightningUrl().BaseUri}");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (_ListenedInvoices.IsEmpty)
                Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): No more invoice to listen on {supportedPaymentMethod.GetLightningUrl().BaseUri}, releasing the connection.");
        }

        public DateTimeOffset? LastFullPoll { get; set; }

        internal async Task PollAllListenedInvoices(CancellationToken cancellation)
        {
            foreach (var invoice in _ListenedInvoices.Values)
            {
                var status = await PollPayment(invoice, cancellation);
                if (status is null ||
                        status is LightningInvoiceStatus.Paid ||
                        status is LightningInvoiceStatus.Expired)
                    _ListenedInvoices.TryRemove(invoice.PaymentMethodDetails.InvoiceId, out var _);
            }
            LastFullPoll = DateTimeOffset.UtcNow;
            if (_ListenedInvoices.IsEmpty)
            {
                StopListeningCancellationTokenSource?.Cancel();
            }
        }

        bool _ErrorAlreadyLogged = false;
        ConcurrentDictionary<string, ListenedInvoice> _ListenedInvoices = new ConcurrentDictionary<string, ListenedInvoice>();

        public async Task<bool> AddPayment(LightningInvoice notification, string invoiceId)
        {
            var payment = await invoiceRepository.AddPayment(invoiceId, notification.PaidAt.Value, new LightningLikePaymentData()
            {
                BOLT11 = notification.BOLT11,
                PaymentHash = BOLT11PaymentRequest.Parse(notification.BOLT11, network.NBitcoinNetwork).PaymentHash,
                Amount = notification.AmountReceived ?? notification.Amount, // if running old version amount received might be unavailable
            }, network, accounted: true);
            if (payment != null)
            {
                var invoice = await invoiceRepository.GetInvoice(invoiceId);
                if (invoice != null)
                    _eventAggregator.Publish(new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) { Payment = payment });
            }
            return payment != null;
        }

        internal void RemoveExpiredInvoices()
        {
            foreach (var invoice in _ListenedInvoices)
            {
                if (invoice.Value.IsExpired())
                    _ListenedInvoices.TryRemove(invoice.Key, out var _);
            }
            if (_ListenedInvoices.IsEmpty)
                StopListeningCancellationTokenSource?.Cancel();
        }
    }

    class ListenedInvoice
    {
        public bool IsExpired() { return DateTimeOffset.UtcNow > Expiration; }
        public DateTimeOffset Expiration { get; set; }
        public LightningLikePaymentMethodDetails PaymentMethodDetails { get; set; }
        public LightningSupportedPaymentMethod SupportedPaymentMethod { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string Uri { get; internal set; }
        public BTCPayNetwork Network { get; internal set; }
        public string InvoiceId { get; internal set; }
    }
}
