using System.Collections.Generic;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public enum InvoiceEventCode : int
    {
        Created = 1001,
        ReceivedPayment = 1002,
        PaymentSettled = 1014,
        PaidInFull = 1003,
        Expired = 1004,
        Confirmed = 1005,
        Completed = 1006,
        MarkedInvalid = 1008,
        FailedToConfirm = 1013,
        PaidAfterExpiration = 1009,
        ExpiredPaidPartial = 2000,
        MarkedCompleted = 2008,
    }
    public class InvoiceEvent : IHasInvoiceId
    {
        public const string Created = "invoice_created";
        public const string ReceivedPayment = "invoice_receivedPayment";
        public const string PaymentSettled = "invoice_paymentSettled";
        public const string MarkedCompleted = "invoice_markedComplete";
        public const string MarkedInvalid = "invoice_markedInvalid";
        public const string Expired = "invoice_expired";
        public const string ExpiredPaidPartial = "invoice_expiredPaidPartial";
        public const string PaidInFull = "invoice_paidInFull";
        public const string PaidAfterExpiration = "invoice_paidAfterExpiration";
        public const string FailedToConfirm = "invoice_failedToConfirm";
        public const string Confirmed = "invoice_confirmed";
        public const string Completed = "invoice_completed";

        public string InvoiceId => Invoice.Id;
        public static Dictionary<string, InvoiceEventCode> EventCodes = new Dictionary<string, InvoiceEventCode>()
        {
            {Created, InvoiceEventCode.Created},
            {ReceivedPayment, InvoiceEventCode.ReceivedPayment},
            {PaymentSettled, InvoiceEventCode.PaymentSettled},
            {PaidInFull, InvoiceEventCode.PaidInFull},
            {Expired, InvoiceEventCode.Expired},
            {Confirmed, InvoiceEventCode.Confirmed},
            {Completed, InvoiceEventCode.Completed},
            {MarkedInvalid, InvoiceEventCode.MarkedInvalid},
            {FailedToConfirm, InvoiceEventCode.FailedToConfirm},
            {PaidAfterExpiration, InvoiceEventCode.PaidAfterExpiration},
            {ExpiredPaidPartial, InvoiceEventCode.ExpiredPaidPartial},
            {MarkedCompleted, InvoiceEventCode.MarkedCompleted},
        };

        public InvoiceEvent(InvoiceEntity invoice, string name)
        {
            Invoice = invoice;
            EventCode = EventCodes[name];
            Name = name;
        }

        public InvoiceEntity Invoice { get; set; }
        public InvoiceEventCode EventCode { get; set; }
        public string Name { get; set; }

        public PaymentEntity Payment { get; set; }
        /// <summary>
        /// Only set for Expired event
        /// </summary>
        public bool PaidPartial { get; internal set; }

        public override string ToString()
        {
            return $"Invoice {Invoice.Id} new event: {Name} ({(int)EventCode})";
        }
    }
}
