using System.Collections.Generic;

namespace BTCPayServer.Payments.External;

public class ExternalPaymentData
{
    public string Reference { get; set; } = string.Empty;
    public string PaymentUrl { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string SettlementCurrency { get; set; } = string.Empty;
    public decimal? SettlementAmount { get; set; }
    public bool RateWasProvidedByCaller { get; set; }
    public string RegisteredBy { get; set; } = string.Empty;
}

public class ExternalPaymentMethodConfig
{
    public string Description { get; set; }
}


public class ExternalPaymentPromptDetails
{
}
