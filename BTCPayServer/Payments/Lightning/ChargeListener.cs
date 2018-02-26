using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using NBXplorer;

namespace BTCPayServer.Payments.Lightning
{
    public class ChargeListener : IHostedService
    {
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
                    var invoice = await _InvoiceRepository.GetInvoice(null, inv.InvoiceId);
                    await Task.WhenAll(invoice.GetPaymentMethods(_NetworkProvider)
                                              .Where(c => c.GetId().PaymentType == PaymentTypes.LightningLike)
                                              .Select(s => Listen(invoice, s, _NetworkProvider.GetNetwork(s.GetId().CryptoCode)))).ConfigureAwait(false);
                }
            }));
            return Task.CompletedTask;
        }

        //MultiValueDictionary<string,string>
        private async Task Listen(InvoiceEntity invoice, PaymentMethod paymentMethod, BTCPayNetwork network)
        {
            var lightningMethod = paymentMethod.GetPaymentMethodDetails() as LightningLikePaymentMethodDetails;
            if (lightningMethod == null)
                return;
            var lightningSupportedMethod = invoice.GetSupportedPaymentMethod<LightningSupportedPaymentMethod>(_NetworkProvider)
                                                  .FirstOrDefault(c => c.CryptoCode == network.CryptoCode);
            if (lightningSupportedMethod == null)
                return;

            var charge = new ChargeClient(lightningSupportedMethod.GetLightningChargeUrl(true), network.NBitcoinNetwork);
            var session = await charge.Listen();
            while (true)
            {
                var notification = await session.NextEvent();
                if (notification.Id == lightningMethod.InvoiceId &&
                   notification.PaymentRequest == lightningMethod.BOLT11)
                {
                    if (notification.Status == "paid" && notification.PaidAt.HasValue)
                    {
                        await _InvoiceRepository.AddPayment(invoice.Id, notification.PaidAt.Value, new LightningLikePaymentData()
                        {
                            BOLT11 = notification.PaymentRequest,
                            Amount = notification.MilliSatoshi
                        }, network.CryptoCode, accounted: true);
                        _Aggregator.Publish(new InvoiceEvent(invoice.Id, 1002, "invoice_receivedPayment"));
                        break;
                    }
                    if(notification.Status == "expired")
                    {
                        break;
                    }
                }
            }
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            return Task.CompletedTask;
        }
    }
}
