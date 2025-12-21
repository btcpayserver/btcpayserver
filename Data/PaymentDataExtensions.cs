#nullable enable
using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentDataExtensions
    {
        public static PaymentData Set(this PaymentData paymentData, InvoiceEntity invoiceEntity, IPaymentMethodHandler handler, object details)
        {
            var prompt = invoiceEntity.GetPaymentPrompt(handler.PaymentMethodId) ?? throw new InvalidOperationException($"Payment prompt for {handler.PaymentMethodId} is not found");
            var paymentBlob = new PaymentBlob()
            {
                Destination = prompt.Destination,
                PaymentMethodFee = prompt.PaymentMethodFee,
                Divisibility = prompt.Divisibility
            }.SetDetails(handler, details);
            paymentData.InvoiceDataId = invoiceEntity.Id;
            paymentData.SetBlob(handler.PaymentMethodId, paymentBlob);
            return paymentData;
        }
        public static PaymentEntity SetBlob(this PaymentData paymentData, PaymentEntity entity)
        {
            paymentData.Amount = entity.Value;
            paymentData.Currency = entity.Currency;
            paymentData.Status = entity.Status;
            paymentData.SetBlob(entity.PaymentMethodId, (PaymentBlob)entity);
            return entity;
        }
        public static PaymentData SetBlob(this PaymentData paymentData, PaymentMethodId paymentMethodId, PaymentBlob blob)
        {
            paymentData.PaymentMethodId = paymentMethodId.ToString();
            paymentData.Blob2 = JToken.FromObject(blob, InvoiceDataExtensions.DefaultSerializer).ToString(Newtonsoft.Json.Formatting.None);
            return paymentData;
        }
        public static PaymentEntity GetBlob(this PaymentData paymentData)
        {
            var entity = JToken.Parse(paymentData.Blob2).ToObject<PaymentEntity>(InvoiceDataExtensions.DefaultSerializer) ?? throw new FormatException($"Invalid {nameof(PaymentEntity)}");
            entity.Status = paymentData.Status!.Value;
            entity.Currency = paymentData.Currency;
            entity.PaymentMethodId = PaymentMethodId.Parse(paymentData.PaymentMethodId);
            entity.Value = paymentData.Amount!.Value;
            entity.Id = paymentData.Id;
            entity.ReceivedTime = paymentData.Created!.Value;
            return entity;
        }
    }
}
