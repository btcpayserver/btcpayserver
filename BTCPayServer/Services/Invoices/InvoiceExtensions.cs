using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public static class InvoiceExtensions
    {

        public static async Task ActivateInvoicePaymentMethod(this InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator, BTCPayNetworkProvider btcPayNetworkProvider, PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            StoreData store,InvoiceEntity invoice, PaymentMethodId paymentMethodId)
        {
            var eligibleMethodToActivate = invoice.GetPaymentMethod(paymentMethodId);
            if (!eligibleMethodToActivate.GetPaymentMethodDetails().Activated)
            {
                var payHandler = paymentMethodHandlerDictionary[paymentMethodId];
                var supportPayMethod = invoice.GetSupportedPaymentMethod()
                    .Single(method => method.PaymentId == paymentMethodId);
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var network = btcPayNetworkProvider.GetNetwork(paymentMethodId.CryptoCode);
                var prepare = payHandler.PreparePayment(supportPayMethod, store, network);
                InvoiceLogs logs = new InvoiceLogs();
                try
                {
                    logs.Write($"{paymentMethodId}: Activating", InvoiceEventData.EventSeverity.Info);
                    var newDetails = await
                        payHandler.CreatePaymentMethodDetails(logs, supportPayMethod, paymentMethod, store, network,
                            prepare);
                    eligibleMethodToActivate.SetPaymentMethodDetails(newDetails);
                    await invoiceRepository.UpdateInvoicePaymentMethod(invoice.Id, eligibleMethodToActivate);
                    eventAggregator.Publish(new InvoicePaymentMethodActivated(paymentMethodId, invoice));
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    logs.Write($"{paymentMethodId}: Payment method unavailable ({ex.Message})",
                        InvoiceEventData.EventSeverity.Error);
                }
                catch (Exception ex)
                {
                    logs.Write($"{paymentMethodId}: Unexpected exception ({ex})", InvoiceEventData.EventSeverity.Error);
                }

                await invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            }
        }

        public static async Task<PaymentEntity> AddPaymentAndSendEvents(this InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            InvoiceEntity invoiceEntity, DateTimeOffset date, CryptoPaymentData paymentData, BTCPayNetworkBase network,
            bool accounted = false)
        {
            var paymentEntity =
                await invoiceRepository.AddPayment(invoiceEntity.Id, date, paymentData, network, accounted);
            if (paymentEntity != null)
            {
                eventAggregator.Publish(
                    new InvoiceEvent(invoiceEntity, InvoiceEvent.ReceivedPayment) { Payment = paymentEntity });
            }

            if (paymentEntity != null && paymentData.PaymentConfirmed(paymentEntity, invoiceEntity.SpeedPolicy))
            {
                eventAggregator.Publish(
                    new InvoiceEvent(invoiceEntity, InvoiceEvent.PaymentSettled) { Payment = paymentEntity });
            }

            return paymentEntity;
        }

        public static async Task UpdatePaymentsAndSendEvents(this InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator, List<PaymentEntity> entities, List<InvoiceEntity> invoices)
        {
            var updated = await invoiceRepository.UpdatePayments(entities);

            foreach ((PaymentEntity paymentEntity, CryptoPaymentData oldData, CryptoPaymentData newData, string
                invoiceId) updatedItem in updated)
            {
                var invoice = invoices.First(entity => entity.Id == updatedItem.invoiceId);
                if (!updatedItem.oldData.PaymentConfirmed(updatedItem.paymentEntity, invoice.SpeedPolicy) &&
                    updatedItem.newData.PaymentConfirmed(updatedItem.paymentEntity, invoice.SpeedPolicy))
                {
                    eventAggregator.Publish(
                        new InvoiceEvent(invoice, InvoiceEvent.PaymentSettled) { Payment = updatedItem.paymentEntity });
                }
            }
        }
    }
}
