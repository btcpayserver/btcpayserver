using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBXplorer;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningListener : IHostedService
    {
        public Logs Logs { get; }

        readonly EventAggregator _Aggregator;
        readonly InvoiceRepository _InvoiceRepository;
        private readonly IMemoryCache _memoryCache;
        readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly LightningClientFactoryService lightningClientFactory;
        private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;
        private readonly StoreRepository _storeRepository;
        private readonly PaymentService _paymentService;
        readonly Channel<string> _CheckInvoices = Channel.CreateUnbounded<string>();
        Task _CheckingInvoice;
        readonly Dictionary<(string, string), LightningInstanceListener> _InstanceListeners = new Dictionary<(string, string), LightningInstanceListener>();

        public LightningListener(EventAggregator aggregator,
                              InvoiceRepository invoiceRepository,
                              IMemoryCache memoryCache,
                              BTCPayNetworkProvider networkProvider,
                              LightningClientFactoryService lightningClientFactory,
                              LightningLikePaymentHandler lightningLikePaymentHandler,
                              StoreRepository storeRepository,
                              IOptions<LightningNetworkOptions> options,
                              PaymentService paymentService,
                              Logs logs)
        {
            Logs = logs;
            _Aggregator = aggregator;
            _InvoiceRepository = invoiceRepository;
            _memoryCache = memoryCache;
            _NetworkProvider = networkProvider;
            this.lightningClientFactory = lightningClientFactory;
            _lightningLikePaymentHandler = lightningLikePaymentHandler;
            _storeRepository = storeRepository;
            _paymentService = paymentService;
            Options = options;
        }

        async Task CheckingInvoice(CancellationToken cancellation)
        {
            while (await _CheckInvoices.Reader.WaitToReadAsync(cancellation) &&
                        _CheckInvoices.Reader.TryRead(out var invoiceId))
            {
                try
                {
                    foreach (var listenedInvoice in (await GetListenedInvoices(invoiceId)).Where(i => !i.IsExpired()))
                    {
                        var instanceListenerKey = (listenedInvoice.Network.CryptoCode, GetLightningUrl(listenedInvoice.SupportedPaymentMethod).ToString());
                        if (!_InstanceListeners.TryGetValue(instanceListenerKey, out var instanceListener) ||
                            !instanceListener.IsListening)
                        {
                            instanceListener ??= new LightningInstanceListener(_InvoiceRepository, _Aggregator, lightningClientFactory, listenedInvoice.Network, GetLightningUrl(listenedInvoice.SupportedPaymentMethod), _paymentService, Logs);
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

        private string GetCacheKey(string invoiceId)
        {
            return $"{nameof(GetListenedInvoices)}-{invoiceId}";
        }
        private Task<List<ListenedInvoice>> GetListenedInvoices(string invoiceId)
        {
            return _memoryCache.GetOrCreateAsync(GetCacheKey(invoiceId), async (cacheEntry) =>
           {
               var listenedInvoices = new List<ListenedInvoice>();
               var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
               foreach (var paymentMethod in invoice.GetPaymentMethods()
                                                             .Where(c => new[] { PaymentTypes.LightningLike, LNURLPayPaymentType.Instance }.Contains(c.GetId().PaymentType)))
               {
                   LightningLikePaymentMethodDetails lightningMethod;
                   LightningSupportedPaymentMethod lightningSupportedMethod;
                   switch (paymentMethod.GetPaymentMethodDetails())
                   {
                       case LNURLPayPaymentMethodDetails lnurlPayPaymentMethodDetails:

                           lightningMethod = lnurlPayPaymentMethodDetails;

                           lightningSupportedMethod = lnurlPayPaymentMethodDetails.LightningSupportedPaymentMethod;

                           break;
                       case LightningLikePaymentMethodDetails { Activated: true } lightningLikePaymentMethodDetails:
                           lightningMethod = lightningLikePaymentMethodDetails;
                           lightningSupportedMethod = invoice.GetSupportedPaymentMethod<LightningSupportedPaymentMethod>()
                               .FirstOrDefault(c => c.CryptoCode == paymentMethod.GetId().CryptoCode);

                           break;
                       default:
                           continue;
                   }

                   if (lightningSupportedMethod == null || string.IsNullOrEmpty(lightningMethod.InvoiceId))
                       continue;
                   var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.GetId().CryptoCode);

                   listenedInvoices.Add(new ListenedInvoice()
                   {
                       Expiration = invoice.ExpirationTime,
                       Uri = GetLightningUrl(lightningSupportedMethod).BaseUri.AbsoluteUri,
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

        readonly ConcurrentDictionary<string, LightningInstanceListener> _ListeningInstances = new ConcurrentDictionary<string, LightningInstanceListener>();
        readonly CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_Aggregator.SubscribeAsync<Events.InvoiceEvent>(async inv =>
            {
                if (inv.Name == InvoiceEvent.Created)
                {
                    _CheckInvoices.Writer.TryWrite(inv.Invoice.Id);
                }

                if (inv.Name == InvoiceEvent.ReceivedPayment && inv.Invoice.Status == InvoiceStatusLegacy.New && inv.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                {
                    var pm = inv.Invoice.GetPaymentMethods().First();
                    if (pm.Calculate().Due.GetValue(pm.Network as BTCPayNetwork) > 0m)
                    {
                        await CreateNewLNInvoiceForBTCPayInvoice(inv.Invoice);
                    }
                }
            }));
            leases.Add(_Aggregator.SubscribeAsync<Events.InvoiceDataChangedEvent>(async inv =>
            {
                if (inv.State.Status == InvoiceStatusLegacy.New &&
                    inv.State.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                {
                    var invoice = await _InvoiceRepository.GetInvoice(inv.InvoiceId);
                    await CreateNewLNInvoiceForBTCPayInvoice(invoice);
                }

            }));
            leases.Add(_Aggregator.Subscribe<Events.InvoicePaymentMethodActivated>(inv =>
            {
                if (inv.PaymentMethodId.PaymentType == LightningPaymentType.Instance)
                {
                    _memoryCache.Remove(GetCacheKey(inv.InvoiceId));
                    _CheckInvoices.Writer.TryWrite(inv.InvoiceId);
                }
            }));
            leases.Add(_Aggregator.Subscribe<Events.InvoiceNewPaymentDetailsEvent>(inv =>
            {
                if (inv.PaymentMethodId.PaymentType == LNURLPayPaymentType.Instance)
                {
                    _memoryCache.Remove(GetCacheKey(inv.InvoiceId));
                    _CheckInvoices.Writer.TryWrite(inv.InvoiceId);
                }
            }));
            _CheckingInvoice = CheckingInvoice(_Cts.Token);
            _ListenPoller = new Timer(async s =>
            {
                try
                {
                    var invoiceIds = await _InvoiceRepository.GetPendingInvoiceIds();
                    foreach (var invoiceId in invoiceIds)
                        _CheckInvoices.Writer.TryWrite(invoiceId);
                }
                catch { } // Never throw an unhandled exception on async void

            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        private async Task CreateNewLNInvoiceForBTCPayInvoice(InvoiceEntity invoice)
        {
            var paymentMethods = invoice.GetPaymentMethods()
                .Where(method => new[] { PaymentTypes.LightningLike, LNURLPayPaymentType.Instance }.Contains(method.GetId().PaymentType))
                .ToArray();
            var store = await _storeRepository.FindStore(invoice.StoreId);
            if (paymentMethods.Any())
            {
                var logs = new InvoiceLogs();
                logs.Write(
                    "Partial payment detected, attempting to update all lightning payment methods with new bolt11 with correct due amount.",
                    InvoiceEventData.EventSeverity.Info);
                foreach (var paymentMethod in paymentMethods)
                {
                    try
                    {
                        var oldDetails = (LightningLikePaymentMethodDetails)paymentMethod.GetPaymentMethodDetails();
                        if (!oldDetails.Activated)
                        {
                            continue;
                        }


                        if (oldDetails is LNURLPayPaymentMethodDetails lnurlPayPaymentMethodDetails && !string.IsNullOrEmpty(lnurlPayPaymentMethodDetails.BOLT11))
                        {
                            try
                            {
                                var client = _lightningLikePaymentHandler.CreateLightningClient(lnurlPayPaymentMethodDetails.LightningSupportedPaymentMethod,
                                    (BTCPayNetwork)paymentMethod.Network);
                                await client.CancelInvoice(oldDetails.InvoiceId);
                            }
                            catch
                            {
                                //not a fully supported option
                            }

                            lnurlPayPaymentMethodDetails = new LNURLPayPaymentMethodDetails()
                            {
                                Activated = lnurlPayPaymentMethodDetails.Activated,
                                Bech32Mode = lnurlPayPaymentMethodDetails.Bech32Mode,
                                InvoiceId = null,
                                NodeInfo = lnurlPayPaymentMethodDetails.NodeInfo,
                                GeneratedBoltAmount = null,
                                BOLT11 = null,
                                LightningSupportedPaymentMethod = lnurlPayPaymentMethodDetails.LightningSupportedPaymentMethod,
                                BTCPayInvoiceId = lnurlPayPaymentMethodDetails.BTCPayInvoiceId
                            };
                            await _InvoiceRepository.NewPaymentDetails(invoice.Id, lnurlPayPaymentMethodDetails,
                                paymentMethod.Network);

                            _Aggregator.Publish(new Events.InvoiceNewPaymentDetailsEvent(invoice.Id,
                                lnurlPayPaymentMethodDetails, paymentMethod.GetId()));

                            continue;
                        }

                        LightningSupportedPaymentMethod supportedMethod = invoice
                            .GetSupportedPaymentMethod<LightningSupportedPaymentMethod>(paymentMethod.GetId()).First();

                        try
                        {
                            var client = _lightningLikePaymentHandler.CreateLightningClient(supportedMethod,
                                (BTCPayNetwork)paymentMethod.Network);
                            await client.CancelInvoice(oldDetails.InvoiceId);
                        }
                        catch
                        {
                            //not a fully supported option
                        }

                        var prepObj =
                            _lightningLikePaymentHandler.PreparePayment(supportedMethod, store, paymentMethod.Network);
                        
                        var pmis = invoice.GetPaymentMethods().Select(method => method.GetId()).ToHashSet();
                        var newPaymentMethodDetails =
                            (LightningLikePaymentMethodDetails)(await _lightningLikePaymentHandler
                                .CreatePaymentMethodDetails(logs, supportedMethod, paymentMethod, store,
                                    paymentMethod.Network, prepObj, pmis));

                        var instanceListenerKey = (paymentMethod.Network.CryptoCode,
                            GetLightningUrl(supportedMethod).ToString());
                        if (_InstanceListeners.TryGetValue(instanceListenerKey, out var instanceListener))
                        {
                            await _InvoiceRepository.NewPaymentDetails(invoice.Id, newPaymentMethodDetails,
                                paymentMethod.Network);

                            instanceListener.AddListenedInvoice(new ListenedInvoice()
                            {
                                Expiration = invoice.ExpirationTime,
                                Uri = GetLightningUrl(supportedMethod).BaseUri.AbsoluteUri,
                                PaymentMethodDetails = newPaymentMethodDetails,
                                SupportedPaymentMethod = supportedMethod,
                                PaymentMethod = paymentMethod,
                                Network = (BTCPayNetwork)paymentMethod.Network,
                                InvoiceId = invoice.Id
                            });

                            _Aggregator.Publish(new Events.InvoiceNewPaymentDetailsEvent(invoice.Id,
                                newPaymentMethodDetails, paymentMethod.GetId()));
                        }
                    }
                    catch (Exception e)
                    {
                        logs.Write($"Could not update {paymentMethod.GetId().ToPrettyString()}: {e.Message}",
                            InvoiceEventData.EventSeverity.Error);
                    }
                }

                await _InvoiceRepository.AddInvoiceLogs(invoice.Id, logs);
                _CheckInvoices.Writer.TryWrite(invoice.Id);
            }

        }

        private LightningConnectionString GetLightningUrl(LightningSupportedPaymentMethod supportedMethod)
        {
            var url = supportedMethod.GetExternalLightningUrl();
            if (url != null)
                return url;
            if (Options.Value.InternalLightningByCryptoCode.TryGetValue(supportedMethod.CryptoCode, out var conn))
                return conn;
            throw new InvalidOperationException($"{supportedMethod.CryptoCode}: The internal lightning node is not set up");
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

        public IOptions<LightningNetworkOptions> Options { get; }

        readonly CancellationTokenSource _Cts = new CancellationTokenSource();
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
        public Logs Logs { get; }

        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetwork _network;
        private readonly PaymentService _paymentService;
        private readonly LightningClientFactoryService _lightningClientFactory;

        public LightningConnectionString ConnectionString { get; }

        public LightningInstanceListener(InvoiceRepository invoiceRepository,
                                        EventAggregator eventAggregator,
                                        LightningClientFactoryService lightningClientFactory,
                                        BTCPayNetwork network,
                                        LightningConnectionString connectionString,
                                        PaymentService paymentService,
                                        Logs logs)
        {
            ArgumentNullException.ThrowIfNull(connectionString);
            Logs = logs;
            this._invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            this._network = network;
            _paymentService = paymentService;
            _lightningClientFactory = lightningClientFactory;
            ConnectionString = connectionString;
        }
        internal bool AddListenedInvoice(ListenedInvoice invoice)
        {
            return _ListenedInvoices.TryAdd(invoice.PaymentMethodDetails.InvoiceId, invoice);
        }

        internal async Task<LightningInvoiceStatus?> PollPayment(ListenedInvoice listenedInvoice, CancellationToken cancellation)
        {
            var client = _lightningClientFactory.Create(ConnectionString, _network);
            LightningInvoice lightningInvoice = await client.GetInvoice(listenedInvoice.PaymentMethodDetails.InvoiceId, cancellation);
            if (lightningInvoice?.Status is LightningInvoiceStatus.Paid &&
                await AddPayment(lightningInvoice, listenedInvoice.InvoiceId, listenedInvoice.PaymentMethod.GetId().PaymentType))
            {
                Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Payment detected via polling on {listenedInvoice.InvoiceId}");
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
            Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Start listening {ConnectionString.BaseUri}");
            try
            {
                var lightningClient = _lightningClientFactory.Create(ConnectionString, _network);
                using var session = await lightningClient.Listen(cancellation);
                // Just in case the payment arrived after our last poll but before we listened.
                await PollAllListenedInvoices(cancellation);
                if (_ErrorAlreadyLogged)
                {
                    Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Could reconnect successfully to {ConnectionString.BaseUri}");
                }
                _ErrorAlreadyLogged = false;
                while (!_ListenedInvoices.IsEmpty)
                {
                    var notification = await session.WaitInvoice(cancellation);
                    if (!_ListenedInvoices.TryGetValue(notification.Id, out var listenedInvoice))
                        continue;
                    if (notification.Id == listenedInvoice.PaymentMethodDetails.InvoiceId &&
                        (notification.BOLT11 == listenedInvoice.PaymentMethodDetails.BOLT11 ||
                         BOLT11PaymentRequest.Parse(notification.BOLT11, _network.NBitcoinNetwork).PaymentHash ==
                         listenedInvoice.PaymentMethodDetails.GetPaymentHash(_network.NBitcoinNetwork)))
                    {
                        if (notification.Status == LightningInvoiceStatus.Paid &&
                            notification.PaidAt.HasValue && notification.Amount != null)
                        {
                            if (await AddPayment(notification, listenedInvoice.InvoiceId, listenedInvoice.PaymentMethod.GetId().PaymentType))
                            {
                                Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Payment detected via notification ({listenedInvoice.InvoiceId})");
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
            catch (Exception ex) when (!cancellation.IsCancellationRequested && !_ErrorAlreadyLogged)
            {
                _ErrorAlreadyLogged = true;
                Logs.PayServer.LogError(ex, $"{_network.CryptoCode} (Lightning): Error while contacting {ConnectionString.BaseUri}");
                Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Stop listening {ConnectionString.BaseUri}");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (_ListenedInvoices.IsEmpty)
                Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): No more invoice to listen on {ConnectionString.BaseUri}, releasing the connection.");
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
        readonly ConcurrentDictionary<string, ListenedInvoice> _ListenedInvoices = new ConcurrentDictionary<string, ListenedInvoice>();

        public async Task<bool> AddPayment(LightningInvoice notification, string invoiceId, PaymentType paymentType)
        {
            var payment = await _paymentService.AddPayment(invoiceId, notification.PaidAt.Value, new LightningLikePaymentData()
            {
                BOLT11 = notification.BOLT11,
                PaymentHash = BOLT11PaymentRequest.Parse(notification.BOLT11, _network.NBitcoinNetwork).PaymentHash,
                Amount = notification.AmountReceived ?? notification.Amount, // if running old version amount received might be unavailable,
                PaymentType = paymentType.ToString()
            }, _network, accounted: true);
            if (payment != null)
            {
                var invoice = await _invoiceRepository.GetInvoice(invoiceId);
                if (invoice != null)
                    _eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
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
