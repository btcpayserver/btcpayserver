#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Amazon.Runtime.Internal;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;

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
        private readonly StoreRepository _storeRepository;
        private readonly PaymentService _paymentService;
        private readonly PaymentMethodHandlerDictionary _handlers;
        readonly Channel<string> _CheckInvoices = Channel.CreateUnbounded<string>();
        Task? _CheckingInvoice;
        readonly Dictionary<(string, string), LightningInstanceListener> _InstanceListeners = new Dictionary<(string, string), LightningInstanceListener>();

        public LightningListener(EventAggregator aggregator,
                              InvoiceRepository invoiceRepository,
                              IMemoryCache memoryCache,
                              BTCPayNetworkProvider networkProvider,
                              LightningClientFactoryService lightningClientFactory,
                              StoreRepository storeRepository,
                              IOptions<LightningNetworkOptions> options,
                              PaymentService paymentService,
                              PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
                              Logs logs)
        {
            Logs = logs;
            _Aggregator = aggregator;
            _InvoiceRepository = invoiceRepository;
            _memoryCache = memoryCache;
            _NetworkProvider = networkProvider;
            this.lightningClientFactory = lightningClientFactory;
            _storeRepository = storeRepository;
            _paymentService = paymentService;
            _handlers = paymentMethodHandlerDictionary;
            Options = options;
        }

        bool needCheckOfflinePayments = true;
        async Task CheckingInvoice(CancellationToken cancellation)
        {
retry:
            try
            {
                Logs.PayServer.LogInformation("Checking if any payment arrived on lightning while the server was offline...");
                foreach (var invoice in await _InvoiceRepository.GetPendingInvoices(cancellationToken: cancellation))
                {
                    if (GetListenedInvoices(invoice).Count > 0)
                    {
                        _CheckInvoices.Writer.TryWrite(invoice.Id);
                        _memoryCache.Set(GetCacheKey(invoice.Id), invoice, GetExpiration(invoice));
                    }
                }
                needCheckOfflinePayments = false;
                Logs.PayServer.LogInformation("Processing lightning payments...");



                while (await _CheckInvoices.Reader.WaitToReadAsync(cancellation) &&
                            _CheckInvoices.Reader.TryRead(out var invoiceId))
                {
                    var invoice = await GetInvoice(invoiceId);

                    foreach (var listenedInvoice in GetListenedInvoices(invoice))
                    {
                        var store = await GetStore(invoice.StoreId);
                        var lnConfig = _handlers.GetLightningConfig(store, listenedInvoice.Network);
                        if (lnConfig is null)
                            continue;
                        var connStr = GetLightningUrl(listenedInvoice.Network.CryptoCode, lnConfig);
                        if (connStr is null)
                            continue;
                        var instanceListenerKey = (listenedInvoice.Network.CryptoCode, connStr.ToString());
                        lock (_InstanceListeners)
                        {
                            if (!_InstanceListeners.TryGetValue(instanceListenerKey, out var instanceListener))
                            {
                                instanceListener ??= new LightningInstanceListener(_InvoiceRepository, _Aggregator, lightningClientFactory, listenedInvoice.Network, _handlers, connStr, _paymentService, Logs);
                                _InstanceListeners.TryAdd(instanceListenerKey, instanceListener);
                            }
                            instanceListener.AddListenedInvoice(listenedInvoice);
                            _ = instanceListener.PollPayment(listenedInvoice, cancellation);
                        }
                    }

                    if (_CheckInvoices.Reader.Count is 0)
                        this.CheckConnections();
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await Task.Delay(1000, cancellation);
                Logs.PayServer.LogWarning(ex, "Unhandled error in the LightningListener");
                goto retry;
            }
        }


        private string GetCacheKey(string invoiceId)
        {
            return $"{nameof(GetListenedInvoices)}-{invoiceId}";
        }
        private Task<InvoiceEntity> GetInvoice(string invoiceId)
        {
            return _memoryCache.GetOrCreateAsync(GetCacheKey(invoiceId), async (cacheEntry) =>
            {
                var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
                cacheEntry.AbsoluteExpiration = GetExpiration(invoice);
                return invoice;
            })!;
        }

        private Task<Data.StoreData> GetStore(string storeId)
        {
            return _memoryCache.GetOrCreateAsync(GetCacheKey("store-" + storeId), async (cacheEntry) =>
            {
                var store = await _storeRepository.FindStore(storeId);
                cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1.0);
                return store;
            })!;
        }

        private static DateTimeOffset GetExpiration(InvoiceEntity invoice)
        {
            var expiredIn = DateTimeOffset.UtcNow - invoice.ExpirationTime;
            return DateTimeOffset.UtcNow + (expiredIn >= TimeSpan.FromMinutes(5.0) ? expiredIn : TimeSpan.FromMinutes(5.0));
        }


        IEnumerable<(IPaymentMethodHandler Handler, PaymentPrompt PaymentPrompt, object? Details)> GetLightningPrompts(InvoiceEntity invoice)
        {
            foreach (var prompt in invoice.GetPaymentPrompts())
            {
                if (!prompt.Activated)
                    continue;
                if (!_handlers.TryGetValue(prompt.PaymentMethodId, out var handler))
                    continue;
                if (handler is ILightningPaymentHandler)
                    yield return (handler, prompt, handler.ParsePaymentPromptDetails(prompt.Details));
            }
        }
        private List<ListenedInvoice> GetListenedInvoices(InvoiceEntity invoice)
        {
            var listenedInvoices = new List<ListenedInvoice>();
            foreach (var o in GetLightningPrompts(invoice))
            {
                if (o.Details is not LigthningPaymentPromptDetails { InvoiceId: not null } ligthningDetails)
                    continue;
                listenedInvoices.Add(new ListenedInvoice(
                    invoice.ExpirationTime,
                    ligthningDetails,
                    o.PaymentPrompt,
                    ((IHasNetwork)o.Handler).Network,
                    invoice.Id));
            }
            return listenedInvoices;
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

                if (inv.Name == InvoiceEvent.ReceivedPayment && inv.Invoice.Status == InvoiceStatus.New && inv.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                {
                    var pm = inv.Invoice.GetPaymentPrompts().First();
                    if (pm.Calculate().Due > 0m)
                    {
                        await CreateNewLNInvoiceForBTCPayInvoice(inv.Invoice);
                    }
                }
            }));
            leases.Add(_Aggregator.SubscribeAsync<Events.InvoiceDataChangedEvent>(async inv =>
            {
                if (inv.State.Status == InvoiceStatus.New &&
                    inv.State.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                {
                    var invoice = await _InvoiceRepository.GetInvoice(inv.InvoiceId);
                    await CreateNewLNInvoiceForBTCPayInvoice(invoice);
                }

            }));
            leases.Add(_Aggregator.Subscribe<Events.InvoicePaymentMethodActivated>(inv =>
            {
                if (_handlers.TryGet(inv.PaymentMethodId) is LightningLikePaymentHandler)
                {
                    _memoryCache.Remove(GetCacheKey(inv.InvoiceId));
                    _CheckInvoices.Writer.TryWrite(inv.InvoiceId);
                }
            }));
            leases.Add(_Aggregator.Subscribe<Events.InvoiceNewPaymentDetailsEvent>(inv =>
            {
                if (_handlers.TryGet(inv.PaymentMethodId) is LNURLPayPaymentHandler && !string.IsNullOrEmpty(inv.InvoiceId))
                {
                    _memoryCache.Remove(GetCacheKey(inv.InvoiceId));
                    _CheckInvoices.Writer.TryWrite(inv.InvoiceId);
                }
            }));
            _CheckingInvoice = CheckingInvoice(_Cts.Token);
            _ListenPoller = new Timer(s =>
            {
                if (needCheckOfflinePayments)
                    return;
                try
                {
                    CheckConnections();
                }
                catch { }

            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        private void CheckConnections()
        {
            lock (_InstanceListeners)
            {
                foreach (var key in _InstanceListeners.Keys)
                {
                    CheckConnection(key.Item1, key.Item2);
                }
            }
        }

        public void CheckConnection(string cryptoCode, string connStr)
        {
            if (_InstanceListeners.TryGetValue((cryptoCode, connStr), out var instance))
            {
                
                instance.RemoveExpiredInvoices();
                if (!instance.Empty)
                    instance.EnsureListening(_Cts.Token);
            }
            }

        private async Task CreateNewLNInvoiceForBTCPayInvoice(InvoiceEntity invoice)
        {
            var paymentMethods = GetLightningPrompts(invoice).ToArray();
            var store = await _storeRepository.FindStore(invoice.StoreId);
            if (store is null)
                return;
            if (paymentMethods.Any())
            {
                var logs = new InvoiceLogs();
                logs.Write(
                    "Partial payment detected, attempting to update all lightning payment methods with new bolt11 with correct due amount.",
                    InvoiceEventData.EventSeverity.Info);
                foreach (var o in paymentMethods)
                {
                    var network = ((IHasNetwork)o.Handler).Network;
                    if (o.Details is not LigthningPaymentPromptDetails oldDetails)
                        continue;
                    var lnConfig = _handlers.GetLightningConfig(store, network);
                    if (lnConfig is null)
                        continue;
                    var connStr = GetLightningUrl(network.CryptoCode, lnConfig);
                    var lightningHandler = _handlers.GetLightningHandler(network);

                    if (connStr is null)
                        continue;
                    try
                    {
                        if (oldDetails is LNURLPayPaymentMethodDetails lnurlPayPaymentMethodDetails)
                        {
                            // LNUrlPay doesn't create a BOLT11 until it's actually scanned.
                            // So if no BOLT11 already created, which is likely the case, do nothing
                            if (string.IsNullOrEmpty(o.PaymentPrompt.Destination))
                                continue;
                            try
                            {
                                var client = lightningHandler.CreateLightningClient(lnConfig);
                                await client.CancelInvoice(oldDetails.InvoiceId);
                            }
                            catch
                            {
                                //not a fully supported option
                            }
                            lnurlPayPaymentMethodDetails = new LNURLPayPaymentMethodDetails()
                            {
                                Bech32Mode = lnurlPayPaymentMethodDetails.Bech32Mode,
                                NodeInfo = lnurlPayPaymentMethodDetails.NodeInfo,
                            };

                            o.PaymentPrompt.Destination = null;
                            o.PaymentPrompt.Details = JToken.FromObject(lnurlPayPaymentMethodDetails, o.Handler.Serializer);
                            await _InvoiceRepository.UpdatePrompt(invoice.Id, o.PaymentPrompt);

                            _Aggregator.Publish(new Events.InvoiceNewPaymentDetailsEvent(invoice.Id,
                                lnurlPayPaymentMethodDetails, o.Handler.PaymentMethodId));

                            continue;
                        }

                        try
                        {
                            var client = lightningHandler.CreateLightningClient(lnConfig);
                            await client.CancelInvoice(oldDetails.InvoiceId);
                        }
                        catch
                        {
                            //not a fully supported option
                        }

                        var paymentContext = new PaymentMethodContext(store, store.GetStoreBlob(), JToken.FromObject(lnConfig, _handlers.GetLightningHandler(network).Serializer), lightningHandler, invoice, logs);
                        var paymentPrompt = paymentContext.Prompt;
                        await paymentContext.BeforeFetchingRates();
                        await paymentContext.CreatePaymentPrompt();
                        if (paymentContext.Status != PaymentMethodContext.ContextStatus.Created)
                            continue;
                        var instanceListenerKey = (paymentPrompt.Currency, connStr.ToString());
                        LightningInstanceListener? instanceListener;
                        lock (_InstanceListeners)
                        {
                            _InstanceListeners.TryGetValue(instanceListenerKey, out instanceListener);
                        }
                        if (instanceListener is not null)
                        {
                            await _InvoiceRepository.NewPaymentPrompt(invoice.Id, paymentContext);
                            await paymentContext.ActivatingPaymentPrompt();
                            var details = lightningHandler.ParsePaymentPromptDetails(paymentPrompt.Details);
                            instanceListener.AddListenedInvoice(new ListenedInvoice(
                                invoice.ExpirationTime,
                                details,
                                paymentPrompt,
                                network,
                                invoice.Id));
                            _Aggregator.Publish(new Events.InvoiceNewPaymentDetailsEvent(invoice.Id,
                                details, paymentPrompt.PaymentMethodId));
                        }
                    }
                    catch (Exception e)
                    {
                        logs.Write($"Could not update {o.Handler.PaymentMethodId.ToString()}: {e.Message}",
                            InvoiceEventData.EventSeverity.Error);
                    }
                }
                await _InvoiceRepository.AddInvoiceLogs(invoice.Id, logs);
                _CheckInvoices.Writer.TryWrite(invoice.Id);
            }

        }

        private string? GetLightningUrl(string cryptoCode, LightningPaymentMethodConfig supportedMethod)
        {
            var url = supportedMethod.GetExternalLightningUrl();
            if (url != null)
                return url;
            return Options.Value.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out var conn) ? conn.ToString() : null;
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
        private Timer? _ListenPoller;

        public IOptions<LightningNetworkOptions> Options { get; }

        readonly CancellationTokenSource _Cts = new CancellationTokenSource();

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            try
            {
                if (_CheckingInvoice != null)
                    await _CheckingInvoice;
            }
            catch (OperationCanceledException)
            {

            }
            try
            {
                await Task.WhenAll(_ListeningInstances.Select(c => c.Value.Listening).Where(c => c != null).ToArray()!);
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
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly PaymentService _paymentService;
        private readonly LightningClientFactoryService _lightningClientFactory;

        public string ConnectionString { get; }

        public LightningInstanceListener(InvoiceRepository invoiceRepository,
                                        EventAggregator eventAggregator,
                                        LightningClientFactoryService lightningClientFactory,
                                        BTCPayNetwork network,
                                        PaymentMethodHandlerDictionary handlers,
                                        string connectionString,
                                        PaymentService paymentService,
                                        Logs logs)
        {
            ArgumentNullException.ThrowIfNull(connectionString);
            Logs = logs;
            this._invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _handlers = handlers;
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
                await AddPayment(lightningInvoice, listenedInvoice.InvoiceId, listenedInvoice.PaymentMethod.PaymentMethodId))
            {
                Logs.PayServer.LogInformation($"{_network.CryptoCode} (Lightning): Payment detected via polling on {listenedInvoice.InvoiceId}");
            }
            return lightningInvoice?.Status;
        }

        public bool Empty => _ListenedInvoices.IsEmpty;
        public bool IsListening => Listening?.Status is TaskStatus.Running || Listening?.Status is TaskStatus.WaitingForActivation;
        public Task? Listening { get; set; }
        public void EnsureListening(CancellationToken cancellation)
        {
            if (!IsListening)
            {
                if (StopListeningCancellationTokenSource != null)
                    StopListeningCancellationTokenSource.Dispose();
                StopListeningCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                Listening = Listen(StopListeningCancellationTokenSource.Token);
            }
        }
        public CancellationTokenSource? StopListeningCancellationTokenSource;
        async Task Listen(CancellationToken cancellation)
        {
            Uri? uri = null;
            string? logUrl = null;
            try
            {
                var lightningClient = _lightningClientFactory.Create(ConnectionString, _network);
                if(lightningClient is null)
                    return;
                uri = lightningClient.GetServerUri();
                logUrl = uri switch
                {
                    null when LightningConnectionStringHelper.ExtractValues(ConnectionString, out var type) is not null => type,
                    null => string.Empty,
                    _ => string.IsNullOrEmpty(uri.UserInfo) ? uri.ToString() : uri.ToString().Replace(uri.UserInfo, "***")
                };
                Logs.PayServer.LogInformation("{CryptoCode} (Lightning): Start listening {Uri}", _network.CryptoCode, logUrl);
                using var session = await lightningClient.Listen(cancellation);
                // Just in case the payment arrived after our last poll but before we listened.
                await PollAllListenedInvoices(cancellation);
                if (_ErrorAlreadyLogged)
                {
                    Logs.PayServer.LogInformation("{CryptoCode} (Lightning): Could reconnect successfully to {Uri}", _network.CryptoCode, logUrl);
                }
                _ErrorAlreadyLogged = false;
                while (!_ListenedInvoices.IsEmpty)
                {
                    var notification = await session.WaitInvoice(cancellation);
                    if (!_ListenedInvoices.TryGetValue(notification.Id, out var listenedInvoice))
                        continue;
                    if (notification.Id == listenedInvoice.PaymentMethodDetails.InvoiceId &&
                        (notification.BOLT11 == listenedInvoice.PaymentMethod.Destination ||
                         BOLT11PaymentRequest.Parse(notification.BOLT11, _network.NBitcoinNetwork).PaymentHash ==
                         GetPaymentHash(listenedInvoice)))
                    {
                        if (notification.Status == LightningInvoiceStatus.Paid &&
                            notification.PaidAt.HasValue && notification.Amount != null)
                        {
                            if (await AddPayment(notification, listenedInvoice.InvoiceId, listenedInvoice.PaymentMethod.PaymentMethodId))
                            {
                                Logs.PayServer.LogInformation("{CryptoCode} (Lightning): Payment detected via notification ({InvoiceId})", _network.CryptoCode, listenedInvoice.InvoiceId);
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
                Logs.PayServer.LogError(ex, "{CryptoCode} (Lightning): Error while contacting {Uri}", _network.CryptoCode, logUrl);
                Logs.PayServer.LogInformation("{CryptoCode} (Lightning): Stop listening {Uri}", _network.CryptoCode, logUrl);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (_ListenedInvoices.IsEmpty)
                Logs.PayServer.LogInformation("{CryptoCode} (Lightning): No more invoice to listen on {Uri}, releasing the connection", _network.CryptoCode, logUrl);
        }

        private uint256? GetPaymentHash(ListenedInvoice listenedInvoice)
        {
            return listenedInvoice.PaymentMethodDetails.PaymentHash ?? BOLT11PaymentRequest.Parse(listenedInvoice.PaymentMethod.Destination, _network.NBitcoinNetwork).PaymentHash;
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

        public async Task<bool> AddPayment(LightningInvoice notification, string invoiceId, PaymentMethodId paymentMethodId)
        {
            var invoiceEntity = await _invoiceRepository.GetInvoice(invoiceId);
            if (notification?.PaidAt is null || invoiceEntity is null)
                return false;

            var handler = _handlers[paymentMethodId];
            var paymentHash = BOLT11PaymentRequest.Parse(notification.BOLT11, _network.NBitcoinNetwork).PaymentHash;
            var paymentData = new PaymentData()
            {
                Id = paymentHash?.ToString() ?? notification.BOLT11,
                Created = notification.PaidAt.Value,
                Status = PaymentStatus.Settled,
                Currency = _network.CryptoCode,
                InvoiceDataId = invoiceId,
                Amount = (notification.AmountReceived ?? notification.Amount).ToDecimal(LightMoneyUnit.BTC),
            }.Set(invoiceEntity, handler, new LightningLikePaymentData()
            {
                PaymentHash = paymentHash,
                Preimage = string.IsNullOrEmpty(notification.Preimage) ? null : uint256.Parse(notification.Preimage),
            });

            var payment = await _paymentService.AddPayment(paymentData, [notification.BOLT11]);
            if (payment != null)
            {
                if (notification.Preimage is not null)
                {
                    var details = (LigthningPaymentPromptDetails)handler.ParsePaymentPromptDetails(invoiceEntity.GetPaymentPrompt(handler.PaymentMethodId)!.Details);
                    if (details.Preimage is null)
                    {
                        details.Preimage = uint256.Parse(notification.Preimage);
                        await _invoiceRepository.UpdatePaymentDetails(invoiceId, handler, details);
                    }
                }
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

    public record ListenedInvoice(
            DateTimeOffset Expiration,
            LigthningPaymentPromptDetails PaymentMethodDetails,
            PaymentPrompt PaymentMethod,
            BTCPayNetwork Network,
            string InvoiceId)
    {
        public bool IsExpired() { return DateTimeOffset.UtcNow > Expiration; }
    }
}
