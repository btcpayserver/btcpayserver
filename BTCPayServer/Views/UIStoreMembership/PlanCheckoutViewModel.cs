using BTCPayServer.Data;
using BTCPayServer.Models;

namespace BTCPayServer.Views.UIStoreMembership;

public class PlanCheckoutViewModel
{
    public StoreBrandingViewModel StoreBranding { get; set; }
    public string Title { get; set; }
    public string StoreName { get; set; }
    public string Email { get; set; }
    public string Id { get; set; }
    public SubscriptionPlanData Data { get; set; }
    public bool IsPrefilled { get; set; }
}
