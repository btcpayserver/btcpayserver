namespace BTCPayServer.Client.Models;

public static class WebhookEventType
{
    public const string InvoiceCreated = nameof(InvoiceCreated);
    public const string InvoiceReceivedPayment = nameof(InvoiceReceivedPayment);
    public const string InvoiceProcessing = nameof(InvoiceProcessing);
    public const string InvoiceExpired = nameof(InvoiceExpired);
    public const string InvoiceSettled = nameof(InvoiceSettled);
    public const string InvoiceInvalid = nameof(InvoiceInvalid);
    public const string InvoicePaymentSettled = nameof(InvoicePaymentSettled);
    public const string PayoutCreated = nameof(PayoutCreated);
    public const string PayoutApproved = nameof(PayoutApproved);
    public const string PayoutUpdated = nameof(PayoutUpdated);
    public const string PaymentRequestUpdated = nameof(PaymentRequestUpdated);
    public const string PaymentRequestCreated = nameof(PaymentRequestCreated);
    public const string PaymentRequestArchived = nameof(PaymentRequestArchived);
    public const string PaymentRequestStatusChanged = nameof(PaymentRequestStatusChanged);
    
}
