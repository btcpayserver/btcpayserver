using System;
using System.Collections.Generic;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentCreation
    {
        public DateTimeOffset Date { get; set; }
        public string PaymentId { get; set; }
        public HashSet<string> SearchTerms { get; } = new HashSet<string>();
        public bool Accounted { get; set; }
        public CryptoPaymentData Details { get; set; }

    }
}
