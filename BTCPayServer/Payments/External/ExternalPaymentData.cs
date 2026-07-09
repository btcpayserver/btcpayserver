namespace BTCPayServer.Payments.External;

public class ExternalPaymentData
{
    public string Reference { get; set; } = string.Empty;
    public string Label { get; set; }
    public string Note { get; set; }
    public string SettlementCurrency { get; set; }
    public decimal? SettlementAmount { get; set; }
    public bool RateWasProvidedByCaller { get; set; }
    public string RegisteredBy { get; set; }
}

public class ExternalPaymentMethodConfig
{
    public string Description { get; set; }
}


public class ExternalPaymentPromptDetails
{
}
