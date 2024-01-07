using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentService
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;

        public PaymentService(EventAggregator eventAggregator,
            ApplicationDbContextFactory applicationDbContextFactory,
            BTCPayNetworkProvider btcPayNetworkProvider, InvoiceRepository invoiceRepository)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
        }
        /// <summary>
        /// Add a payment to an invoice
        /// </summary>
        /// <param name="invoiceId"></param>
        /// <param name="date"></param>
        /// <param name="paymentData"></param>
        /// <param name="cryptoCode"></param>
        /// <param name="accounted"></param>
        /// <returns>The PaymentEntity or null if already added</returns>
        public async Task<PaymentEntity> AddPayment(string invoiceId, DateTimeOffset date, CryptoPaymentData paymentData, BTCPayNetworkBase network, bool accounted = false)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var invoice = await context.Invoices.FindAsync(invoiceId);
            if (invoice == null)
                return null;
            InvoiceEntity invoiceEntity = invoice.GetBlob(_btcPayNetworkProvider);
            PaymentMethod paymentMethod = invoiceEntity.GetPaymentMethod(new PaymentMethodId(network.CryptoCode, paymentData.GetPaymentType()));
            if (paymentMethod is null)
                return null;
            IPaymentMethodDetails paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
            PaymentEntity entity = new PaymentEntity
            {
                Version = 1,
#pragma warning disable CS0618
                Currency = network.CryptoCode,
#pragma warning restore CS0618
                ReceivedTime = date.UtcDateTime,
                Accounted = accounted,
                NetworkFee = paymentMethodDetails.GetNextNetworkFee(),
                Network = network
            };
            entity.SetCryptoPaymentData(paymentData);
            PaymentData data = new PaymentData
            {
                Id = paymentData.GetPaymentId(),
                InvoiceDataId = invoiceId,
                Accounted = accounted
            };
            data.SetBlob(entity);
            await context.Payments.AddAsync(data);

            InvoiceRepository.AddToTextSearch(context, invoice, paymentData.GetSearchTerms());
            var alreadyExists = false;
            try
            {
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (DbUpdateException) { alreadyExists = true; }

            if (alreadyExists)
            {
                return null;
            }

            if (paymentData.PaymentConfirmed(entity, invoiceEntity.SpeedPolicy))
            {
                _eventAggregator.Publish(new InvoiceEvent(invoiceEntity, InvoiceEvent.PaymentSettled) { Payment = entity });
            }
            return entity;
        }

        public async Task UpdatePayments(List<PaymentEntity> payments)
        {
            if (payments.Count == 0)
                return;
            await using var context = _applicationDbContextFactory.CreateContext();
            var paymentsDict = payments
                .Select(entity => (entity, entity.GetCryptoPaymentData()))
                .ToDictionary(tuple => tuple.Item2.GetPaymentId());
            var paymentIds = paymentsDict.Keys.ToArray();
            var dbPayments = await context.Payments
                .Include(data => data.InvoiceData)
                .Where(data => paymentIds.Contains(data.Id)).ToDictionaryAsync(data => data.Id);
            var eventsToSend = new List<InvoiceEvent>();
            foreach (KeyValuePair<string, (PaymentEntity entity, CryptoPaymentData)> payment in paymentsDict)
            {
                var dbPayment = dbPayments[payment.Key];
                var invBlob = _invoiceRepository.ToEntity(dbPayment.InvoiceData);
                var dbPaymentEntity = dbPayment.GetBlob(_btcPayNetworkProvider);
                var wasConfirmed = dbPayment.GetBlob(_btcPayNetworkProvider).GetCryptoPaymentData()
                    .PaymentConfirmed(dbPaymentEntity, invBlob.SpeedPolicy);
                if (!wasConfirmed && payment.Value.Item2.PaymentConfirmed(payment.Value.entity, invBlob.SpeedPolicy))
                {
                    eventsToSend.Add(new InvoiceEvent(invBlob, InvoiceEvent.PaymentSettled) { Payment = payment.Value.entity });
                }

                dbPayment.Accounted = payment.Value.entity.Accounted;
                dbPayment.SetBlob(payment.Value.entity);
            }
            await context.SaveChangesAsync().ConfigureAwait(false);
            eventsToSend.ForEach(_eventAggregator.Publish);
        }

    }
}
