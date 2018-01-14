using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Events
{
    public class InvoicePaymentEvent
    {
        
        public InvoicePaymentEvent(string invoiceId, string cryptoCode, string address)
        {
            InvoiceId = invoiceId;
            Address = address;
            CryptoCode = cryptoCode
        }

        public string Address { get; set; }
        public string CryptoCode { get; private set; }
        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return $"{CryptoCode}: Invoice {InvoiceId} received a payment on {Address}";
        }
    }
}
