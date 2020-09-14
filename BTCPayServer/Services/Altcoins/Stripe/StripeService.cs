using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Stripe.Payments;
using BTCPayServer.Common.Altcoins.Fiat;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = BTCPayServer.Services.Invoices.PaymentMethod;

namespace BTCPayServer.Services.Altcoins.Stripe
{
    public class StripeService : EventHostedServiceBase
    {
        private readonly EventAggregator _eventAggregator;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly ILogger<StripeService> _logger;
        private readonly StripePaymentMethodHandler _stripePaymentMethodHandler;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public StripeService(EventAggregator eventAggregator,
            InvoiceRepository invoiceRepository,
            ILogger<StripeService> logger,
            StripePaymentMethodHandler stripePaymentMethodHandler,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider) : base(eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _invoiceRepository = invoiceRepository;
            _logger = logger;
            _stripePaymentMethodHandler = stripePaymentMethodHandler;
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceDataChangedEvent>();
            Subscribe<InvoiceEvent>();
            Subscribe<HandleStripeWebhookPaymentData>();
            Subscribe<CheckStripePayments>();
            base.SubscribeToEvents();
            _eventAggregator.Publish(new CheckStripePayments());
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is CheckStripePayments)

            {
                await CheckPendingInvoices();
                _ = Task.Delay(TimeSpan.FromMinutes(2), cancellationToken).ContinueWith(task =>
                {
                    _eventAggregator.Publish(new CheckStripePayments());
                }, TaskScheduler.Default);
            }
            else if (evt is InvoiceDataChangedEvent ||
                     (evt is InvoiceEvent invoiceEvent && invoiceEvent.Name == InvoiceEvent.ReceivedPayment))
            {
                var invoiceId = (evt as InvoiceDataChangedEvent)?.InvoiceId ?? (evt as InvoiceEvent)?.Invoice?.Id;
                var invoice = await _invoiceRepository.GetInvoice(invoiceId);
                var methods = invoice.GetPaymentMethods()
                    .Where(method => method.GetId().PaymentType == StripePaymentType.Instance).ToArray();
                if (!methods.Any())
                {
                    return;
                }

                var storeData = await _storeRepository.FindStore(invoice.StoreId);
                foreach (var method in methods)
                {
                    var supportedMethod =
                        invoice.GetSupportedPaymentMethod<StripeSupportedPaymentMethod>(method.GetId())
                            .First();
                    var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(method.GetId().CryptoCode);
                    var details = (StripePaymentMethodDetails)method.GetPaymentMethodDetails();
                    details.Disabled = true;
                    if (!supportedMethod.UseCheckout && !invoice.GetPayments(network).Any(entity =>
                        entity.Accounted && entity.GetPaymentMethodId() == supportedMethod.PaymentId &&
                        entity.GetCryptoPaymentData() is StripePaymentData stripePaymentData &&
                        stripePaymentData.PaymentIntentId == details.PaymentIntentId))
                    {
                        await new PaymentIntentService(new StripeClient(supportedMethod.SecretKey)).CancelAsync(
                            details.PaymentIntentId, cancellationToken: cancellationToken);
                    }

                    if (invoice.Status == InvoiceStatus.New)
                    {
                        var logs = new InvoiceLogs();
                        logs.Write("Need update invoice with new stripe payment instructions.",
                            InvoiceEventData.EventSeverity.Info);
                        try
                        {
                            details = (StripePaymentMethodDetails)await
                                _stripePaymentMethodHandler.CreatePaymentMethodDetails(logs, supportedMethod,
                                    method,
                                    storeData, network,
                                    _stripePaymentMethodHandler.PreparePayment(supportedMethod, storeData,
                                        network));
                        }
                        catch (Exception)
                        {
                            // ignored
                        }

                        await _invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
                        await _invoiceRepository.NewAddress(invoice.Id, details, network);
                    }
                }

                _eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            }
            else if (evt is HandleStripeWebhookPaymentData handleStripeWebhookPaymentData)
            {
                var invoice = await _invoiceRepository.GetInvoice(handleStripeWebhookPaymentData.InvoiceId);
                var pMethod = invoice.GetPaymentMethod(handleStripeWebhookPaymentData.PaymentMethodId);
                if (pMethod is null)
                {
                    return;
                }

                if (pMethod.GetPaymentMethodDetails() is StripePaymentMethodDetails stripePaymentMethodDetails &&
                    !stripePaymentMethodDetails.Disabled &&
                    stripePaymentMethodDetails.SessionId == handleStripeWebhookPaymentData.PaymentData.SessionId &&
                    stripePaymentMethodDetails.PaymentIntentId ==
                    handleStripeWebhookPaymentData.PaymentData.PaymentIntentId)
                {
                    await NewMethod(invoice, pMethod);
                }
            }

            await base.ProcessEvent(evt, cancellationToken);
        }

        private async Task CheckPendingInvoices()
        {
            var invoiceIds = await _invoiceRepository.GetPendingInvoices();
            var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery() {InvoiceId = invoiceIds});
            invoices = invoices.Where(entity =>
                    entity.GetPaymentMethods().Any(method => method.GetId().PaymentType == StripePaymentType.Instance))
                .ToArray();
            _logger.LogInformation($"Updating pending payments for Stripe in {string.Join(',', invoiceIds)}");
            foreach (InvoiceEntity invoice in invoices)
            {
                var methods = invoice.GetPaymentMethods()
                    .Where(method => method.GetId().PaymentType == StripePaymentType.Instance).ToArray();

                foreach (var method in methods)
                {
                    await NewMethod(invoice, method);
                }
            }
        }

        public async Task NewMethod(InvoiceEntity invoice, PaymentMethod method)
        {
            var supportedMethod =
                invoice.GetSupportedPaymentMethod<StripeSupportedPaymentMethod>(method.GetId())
                    .FirstOrDefault();
            var network = _btcPayNetworkProvider.GetNetwork<FiatPayNetwork>(method.GetId().CryptoCode);
            var details = (StripePaymentMethodDetails)method.GetPaymentMethodDetails();
            StripePaymentData data = null;
            if (!string.IsNullOrEmpty(details.SessionId))
            {
                var session =
                    await new SessionService(new StripeClient(supportedMethod.SecretKey)).GetAsync(
                        details.SessionId);
                if (session != null && session.PaymentStatus == "paid" &&
                    session.Metadata.TryGetValue("invoice", out var invoiceId) && invoiceId == invoice.Id)
                {
                    data = new StripePaymentData()
                    {
                        Amount = details.Amount,
                        Network = network,
                        CryptoCode = network.CryptoCode,
                        SessionId = details.SessionId,
                        PaymentIntentId = session.PaymentIntentId,
                    };
                }
            }
            else if (!string.IsNullOrEmpty(details.PaymentIntentId))
            {
                var paymentIntent =
                    await new PaymentIntentService(new StripeClient(supportedMethod.SecretKey)).GetAsync(
                        details.PaymentIntentId);
                if (paymentIntent != null && paymentIntent.Status == "succeeded" &&
                    paymentIntent.Metadata.TryGetValue("invoice", out var invoiceId) && invoiceId == invoice.Id)
                {
                    data = new StripePaymentData()
                    {
                        Amount = details.Amount,
                        Network = network,
                        CryptoCode = network.CryptoCode,
                        SessionId = details.SessionId,
                        PaymentIntentId = details.PaymentIntentId,
                    };
                }
            }

            if (data != null)
            {
                var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.Now, data, network, true);

                _eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) {Payment = payment});
            }
        }

        public class CheckStripePayments
        {
            public override string ToString()
            {
                return "";
            }
        }

        public class HandleStripeWebhookPaymentData
        {
            public string InvoiceId { get; set; }
            public StripePaymentData PaymentData { get; set; }
            public PaymentMethodId PaymentMethodId { get; set; }

            public override string ToString()
            {
                return "";
            }
        }
    }
}
