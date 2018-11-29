using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class InvoiceNewAddressEvent
    {
        public InvoiceNewAddressEvent(string invoiceId, string address, BTCPayNetwork network)
        {
            Address = address;
            InvoiceId = invoiceId;
            Network = network;
        }

        public string Address { get; set; }
        public string InvoiceId { get; set; }
        public BTCPayNetwork Network { get; set; }
        public override string ToString()
        {
            return $"{Network.CryptoCode}: New address {Address} for invoice {InvoiceId}";
        }
    }
}
