namespace BTCPayServer.Plugins.Monetization;

public class MonetizationSettings
{
    public string OfferingId { get; set; }
    public string DefaultPlanId { get; set; }

    public bool IsSetup() => OfferingId is not null && DefaultPlanId is not null;
}
