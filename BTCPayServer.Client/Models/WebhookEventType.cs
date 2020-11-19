using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public enum WebhookEventType
    {
        InvoiceCreated,
        InvoiceReceivedPayment,
        InvoicePaidInFull,
        InvoiceExpired,
        InvoiceConfirmed,
        InvoiceInvalid
    }
}
