using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using NBXplorer;

namespace BTCPayServer.Payments.Lightning
{
    public class ChargeListener : IHostedService
    {
        class ListenedInvoice
        {
            public LightningLikePaymentMethodDetails PaymentMethodDetails { get; set; }
            public LightningSupportedPaymentMethod SupportedPaymentMethod { get; set; }
            public PaymentMethod PaymentMethod { get; set; }
            public string Uri { get; internal set; }
            public BTCPayNetwork Network { get; internal set; }
            public string InvoiceId { get; internal set; }
        }

        EventAggregator _Aggregator;
        InvoiceRepository _InvoiceRepository;
        BTCPayNetworkProvider _NetworkProvider;
        public ChargeListener(EventAggregator aggregator,
                              InvoiceRepository invoiceRepository,
                              BTCPayNetworkProvider networkProvider)
        {
            _Aggregator = aggregator;
            _InvoiceRepository = invoiceRepository;
            _NetworkProvider = networkProvider;
        }

        CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_Aggregator.Subscribe<Events.InvoiceEvent>(async inv =>
            {
                if (inv.Name == "invoice_created")
                {
                    await EnsureListening(inv.InvoiceId, false);
                }
            }));

            _ListenPoller = new Timer(async s =>
            {
                await Task.WhenAll((await _InvoiceRepository.GetPendingInvoices())
                .Select(async invoiceId => await EnsureListening(invoiceId, true))
                .ToArray());
            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        private async Task EnsureListening(string invoiceId, bool poll)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
            foreach (var paymentMethod in invoice.GetPaymentMethods(_NetworkProvider)
                                                          .Where(c => c.GetId().PaymentType == PaymentTypes.LightningLike))
            {
                var lightningMethod = paymentMethod.GetPaymentMethodDetails() as LightningLikePaymentMethodDetails;
                if (lightningMethod == null)
                    continue;
                var lightningSupportedMethod = invoice.GetSupportedPaymentMethod<LightningSupportedPaymentMethod>(_NetworkProvider)
                                          .FirstOrDefault(c => c.CryptoCode == paymentMethod.GetId().CryptoCode);
                if (lightningSupportedMethod == null)
                    continue;
                var network = _NetworkProvider.GetNetwork(paymentMethod.GetId().CryptoCode);

                var listenedInvoice = new ListenedInvoice()
                {
                    Uri = lightningSupportedMethod.GetLightningChargeUrl(false).AbsoluteUri,
                    PaymentMethodDetails = lightningMethod,
                    SupportedPaymentMethod = lightningSupportedMethod,
                    PaymentMethod = paymentMethod,
                    Network = network,
                    InvoiceId = invoice.Id
                };

                if (poll)
                {
                    var charge = GetChargeClient(lightningSupportedMethod, network);
                    var chargeInvoice = await charge.GetInvoice(lightningMethod.InvoiceId);
                    if (chargeInvoice == null)
                        continue;
                    if(chargeInvoice.Status == "paid")
                        await AddPayment(network, chargeInvoice, listenedInvoice);
                    if (chargeInvoice.Status == "paid" || chargeInvoice.Status == "expired")
                        continue;
                }

                if (!Listening(invoiceId))
                {
                    StartListening(listenedInvoice);
                }
            }
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
        private async Task Listen(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            try
            {
                Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Start listening {supportedPaymentMethod.GetLightningChargeUrl(false)}");
                var charge = GetChargeClient(supportedPaymentMethod, network);
                var session = await charge.Listen(_Cts.Token);
                while (true)
                {
                    var notification = await session.NextEvent(_Cts.Token);
                    ListenedInvoice listenedInvoice = GetListenedInvoice(notification.Id);
                    if (listenedInvoice == null)
                        continue;

                    if (notification.Id == listenedInvoice.PaymentMethodDetails.InvoiceId &&
                       notification.PaymentRequest == listenedInvoice.PaymentMethodDetails.BOLT11)
                    {
                        if (notification.Status == "paid" && notification.PaidAt.HasValue)
                        {
                            await AddPayment(network, notification, listenedInvoice);
                            if (DoneListening(listenedInvoice))
                                break;
                        }
                        if (notification.Status == "expired")
                        {
                            if (DoneListening(listenedInvoice))
                                break;
                        }
                    }
                }
            }
            catch when (_Cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, $"{supportedPaymentMethod.CryptoCode} (Lightning): Error while contacting {supportedPaymentMethod.GetLightningChargeUrl(false)}");
            }
            Logs.PayServer.LogInformation($"{supportedPaymentMethod.CryptoCode} (Lightning): Stop listening {supportedPaymentMethod.GetLightningChargeUrl(false)}");
        }

        private async Task AddPayment(BTCPayNetwork network, ChargeInvoice notification, ListenedInvoice listenedInvoice)
        {
            await _InvoiceRepository.AddPayment(listenedInvoice.InvoiceId, notification.PaidAt.Value, new LightningLikePaymentData()
            {
                BOLT11 = notification.PaymentRequest,
                Amount = notification.MilliSatoshi
            }, network.CryptoCode, accounted: true);
            _Aggregator.Publish(new InvoiceEvent(listenedInvoice.InvoiceId, 1002, "invoice_receivedPayment"));
        }

        private static ChargeClient GetChargeClient(LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network)
        {
            return new ChargeClient(supportedPaymentMethod.GetLightningChargeUrl(true), network.NBitcoinNetwork);
        }

        List<Task> _ListeningLightning = new List<Task>();
        MultiValueDictionary<string, ListenedInvoice> _ListenedInvoiceByLightningUrl = new MultiValueDictionary<string, ListenedInvoice>();
        Dictionary<string, ListenedInvoice> _ListenedInvoiceByChargeInvoiceId = new Dictionary<string, ListenedInvoice>();
        HashSet<string> _InvoiceIds = new HashSet<string>();
        private Timer _ListenPoller;

        /// <summary>
        /// Stop listening an invoice
        /// </summary>
        /// <param name="listenedInvoice">The invoice to stop listening</param>
        /// <returns>true if still need to listen the lightning instance</returns>
        bool DoneListening(ListenedInvoice listenedInvoice)
        {
            lock (_ListenedInvoiceByLightningUrl)
            {
                _ListenedInvoiceByChargeInvoiceId.Remove(listenedInvoice.PaymentMethodDetails.InvoiceId);
                _ListenedInvoiceByLightningUrl.Remove(listenedInvoice.Uri, listenedInvoice);
                _InvoiceIds.Remove(listenedInvoice.InvoiceId);
                if (!_ListenedInvoiceByLightningUrl.ContainsKey(listenedInvoice.Uri))
                {
                    return true;
                }
            }
            return false;
        }

        bool Listening(string invoiceId)
        {
            lock(_ListenedInvoiceByLightningUrl)
            {
                return _InvoiceIds.Contains(invoiceId);
            }
        }

        private ListenedInvoice GetListenedInvoice(string chargeInvoiceId)
        {
            ListenedInvoice listenedInvoice = null;
            lock (_ListenedInvoiceByLightningUrl)
            {
                _ListenedInvoiceByChargeInvoiceId.TryGetValue(chargeInvoiceId, out listenedInvoice);
            }
            return listenedInvoice;
        }

        bool StartListening(ListenedInvoice listenedInvoice)
        {
            lock (_ListenedInvoiceByLightningUrl)
            {
                if (_InvoiceIds.Contains(listenedInvoice.InvoiceId))
                    return false;
                if (!_ListenedInvoiceByLightningUrl.ContainsKey(listenedInvoice.Uri))
                {
                    var listen = Listen(listenedInvoice.SupportedPaymentMethod, listenedInvoice.Network);
                    _ListeningLightning.Add(listen);
                    listen.ContinueWith(_ =>
                    {
                        DoneListening(listenedInvoice);
                    }, TaskScheduler.Default);
                }
                _ListenedInvoiceByLightningUrl.Add(listenedInvoice.Uri, listenedInvoice);
                _ListenedInvoiceByChargeInvoiceId.Add(listenedInvoice.PaymentMethodDetails.InvoiceId, listenedInvoice);
                _InvoiceIds.Add(listenedInvoice.InvoiceId);
            }
            return true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            Task[] listening = null;
            lock (_ListenedInvoiceByLightningUrl)
            {
                listening = _ListeningLightning.ToArray();
            }
            await Task.WhenAll(listening);
        }
    }
}
