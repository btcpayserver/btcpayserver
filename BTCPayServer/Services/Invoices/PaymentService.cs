using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentService
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;

        public PaymentService(EventAggregator eventAggregator,
            ApplicationDbContextFactory applicationDbContextFactory,
            PaymentMethodHandlerDictionary handlers,
            InvoiceRepository invoiceRepository)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _handlers = handlers;
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
        public async Task<PaymentEntity> AddPayment(Data.PaymentData paymentData, HashSet<string> searchTerms = null)
        {
            InvoiceEntity invoiceEntity;
            await using (var context = _applicationDbContextFactory.CreateContext())
            {
                var invoice = await context.Invoices.FindAsync(paymentData.InvoiceDataId);
                if (invoice == null)
                    return null;
                invoiceEntity = invoice.GetBlob();
                var pmi = PaymentMethodId.Parse(paymentData.PaymentMethodId);
                PaymentPrompt paymentMethod = invoiceEntity.GetPaymentPrompt(pmi);
                if (paymentMethod is null || !_handlers.TryGetValue(pmi, out var handler))
                    return null;
                await context.Payments.AddAsync(paymentData);

                if (searchTerms is not null)
                    InvoiceRepository.AddToTextSearch(context, invoice, searchTerms.ToArray());
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
            }
            invoiceEntity = await _invoiceRepository.GetInvoice(paymentData.InvoiceDataId);
            var entity = invoiceEntity.GetPayments(false).Single(p => p.Id == paymentData.Id);
            if (paymentData.Status is PaymentStatus.Settled)
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
            var paymentIds = payments.Select(p => p.Id).ToArray();
            var dbPayments = await context.Payments
                .Include(data => data.InvoiceData)
                .Where(data => paymentIds.Contains(data.Id))
                .ToDictionaryAsync(data => data.Id);
            var eventsToSend = new List<InvoiceEvent>();
            foreach (var payment in payments)
            {
                var dbPayment = dbPayments[payment.Id];
                var invBlob = _invoiceRepository.ToEntity(dbPayment.InvoiceData);
                var dbPaymentEntity = dbPayment.GetBlob();
                var wasConfirmed = dbPayment.Status is PaymentStatus.Settled;
                if (!wasConfirmed && payment.Status is PaymentStatus.Settled)
                {
                    eventsToSend.Add(new InvoiceEvent(invBlob, InvoiceEvent.PaymentSettled) { Payment = payment });
                }
                dbPayment.SetBlob(payment);
            }
            await context.SaveChangesAsync().ConfigureAwait(false);
            eventsToSend.ForEach(_eventAggregator.Publish);
        }

    }
}
