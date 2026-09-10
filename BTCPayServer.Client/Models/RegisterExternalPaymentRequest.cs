using System;

namespace BTCPayServer.Client.Models;

public class RegisterExternalPaymentRequest
{
    public decimal Amount { get; set; }
    public string? SettlementCurrency { get; set; }
    public decimal? Rate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? PaymentUrl { get; set; }
    public string? Label { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? Date { get; set; }
}
