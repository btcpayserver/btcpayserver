using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Invoices;
using NBitpayClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Data
{
    public static class InvoiceDataExtensions
    {
        public static readonly JsonSerializerSettings DefaultSerializerSettings;
        public static readonly JsonSerializer DefaultSerializer;
        static InvoiceDataExtensions()
        {
            (DefaultSerializerSettings, DefaultSerializer) = BlobSerializer.CreateSerializer(null as NBitcoin.Network);
        }
        public static void SetBlob(this InvoiceData invoiceData, InvoiceEntity blob)
        {
            if (blob.Metadata is null)
                blob.Metadata = new InvoiceMetadata();
            invoiceData.Created = blob.InvoiceTime;
            invoiceData.Currency = blob.Currency;
            invoiceData.Amount = blob.Price;
            invoiceData.HasTypedBlob<InvoiceEntity>().SetBlob(blob, DefaultSerializer);
        }
#nullable enable
        public static PayoutMethodId? GetClosestPayoutMethodId(this InvoiceData invoice, IEnumerable<PayoutMethodId> pmids)
        {
            var paymentMethodIds = invoice.Payments.Select(o => PaymentMethodId.Parse(o.PaymentMethodId)).ToArray();
            if (paymentMethodIds.Length == 0)
                paymentMethodIds = invoice.GetBlob().GetPaymentPrompts().Select(p => p.PaymentMethodId).ToArray();
            return PaymentMethodId.GetSimilarities(pmids, paymentMethodIds)
                .OrderByDescending(o => o.similarity)
                .Select(o => o.a)
                .FirstOrDefault();
        }
        public static PaymentMethodId? GetClosestPaymentMethodId(this InvoiceEntity invoice, IEnumerable<PayoutMethodId> pmids)
        {
            var paymentMethodIds = invoice.GetPayments(false).Select(o => o.PaymentMethodId).ToArray();
            if (paymentMethodIds.Length == 0)
                paymentMethodIds = invoice.GetPaymentPrompts().Select(p => p.PaymentMethodId).ToArray();
            return PaymentMethodId.GetSimilarities(pmids, paymentMethodIds)
                .OrderByDescending(o => o.similarity)
                .Select(o => o.b)
                .FirstOrDefault();
        }
#nullable restore
        public static InvoiceEntity GetBlob(this InvoiceData invoiceData)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var entity = invoiceData.HasTypedBlob<InvoiceEntity>().GetBlob(DefaultSerializerSettings);
            entity.Payments = invoiceData.Payments?
            .Select(p => p.GetBlob())
            .OrderBy(a => a.ReceivedTime)
            .ToList();
#pragma warning restore CS0618
            var state = invoiceData.GetInvoiceState();
            entity.Id = invoiceData.Id;
            entity.Currency = invoiceData.Currency;
            if (invoiceData.Amount is decimal price)
            {
                entity.Price = price;
            }
            entity.InvoiceTime = invoiceData.Created;
            entity.StoreId = invoiceData.StoreDataId;
            entity.ExceptionStatus = state.ExceptionStatus;
            entity.Status = state.Status;
            if (invoiceData.AddressInvoices != null)
            {
                entity.Addresses = invoiceData.AddressInvoices.Select(a => (PaymentMethodId.Parse(a.PaymentMethodId), a.Address)).ToHashSet();
            }
            if (invoiceData.Refunds != null)
            {
                entity.Refunds = invoiceData.Refunds.OrderBy(c => c.PullPaymentData.StartDate).ToList();
            }
            entity.Archived = invoiceData.Archived;
            entity.UpdateTotals();
            return entity;
        }
        public static InvoiceState GetInvoiceState(this InvoiceData invoiceData)
        {
            return new InvoiceState(invoiceData.Status, invoiceData.ExceptionStatus);
        }
    }
}
