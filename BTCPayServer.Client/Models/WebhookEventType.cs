namespace BTCPayServer.Client.Models
{
    public enum WebhookEventType
    {
        InvoiceCreated,
        InvoiceReceivedPayment,
        InvoiceProcessing,
        InvoiceExpired,
        InvoiceSettled,
        InvoiceInvalid,
        InvoicePaymentSettled,
    }
}
