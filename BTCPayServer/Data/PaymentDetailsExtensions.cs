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
    public static class PaymentDetailsExtensions
    {
        public static PaymentDetails Set(this PaymentDetails paymentDetails, InvoiceEntity invoiceEntity, IPaymentMethodHandler handler, object details)
        {
            var prompt = invoiceEntity.GetPaymentPrompt(handler.PaymentMethodId) ?? throw new InvalidOperationException($"Payment prompt for {handler.PaymentMethodId} is not found");
            var paymentBlob = new PaymentBlob()
            {
                Destination = prompt.Destination,
                PaymentMethodFee = prompt.PaymentMethodFee,
                Divisibility = prompt.Divisibility
            }.SetDetails(handler, details);
            paymentDetails.InvoiceDataId = invoiceEntity.Id;
            paymentDetails.SetBlob(handler.PaymentMethodId, paymentBlob);
            return paymentDetails;
        }
        public static PaymentEntity SetBlob(this PaymentDetails paymentDetails, PaymentEntity entity)
        {
            paymentDetails.Amount = entity.Value;
            paymentDetails.Currency = entity.Currency;
            paymentDetails.Status = entity.Status;
            paymentDetails.SetBlob(entity.PaymentMethodId, (PaymentBlob)entity);
            return entity;
        }
        public static PaymentDetails SetBlob(this PaymentDetails paymentDetails, PaymentMethodId paymentMethodId, PaymentBlob blob)
        {
            paymentDetails.Type = paymentMethodId.ToString();
            paymentDetails.Blob2 = JToken.FromObject(blob, InvoiceDataExtensions.DefaultSerializer).ToString(Newtonsoft.Json.Formatting.None);
            return paymentDetails;
        }

        public static PaymentEntity GetBlob(this PaymentDetails paymentDetails)
        {
            var entity = JToken.Parse(paymentDetails.Blob2).ToObject<PaymentEntity>(InvoiceDataExtensions.DefaultSerializer) ?? throw new FormatException($"Invalid {nameof(PaymentEntity)}");
            entity.Status = paymentDetails.Status!.Value;
            entity.Currency = paymentDetails.Currency;
            entity.PaymentMethodId = PaymentMethodId.Parse(paymentDetails.Type);
            entity.Value = paymentDetails.Amount!.Value;
            entity.Id = paymentDetails.Id;
            entity.ReceivedTime = paymentDetails.Created!.Value;
            return entity;
        }
    }
}
