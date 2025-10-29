using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Plugins.Monetization.Views;

public class MonetizationViewModel
{
    public MonetizationSettings Settings { get; set; }
    public PlanData DefaultPlan { get; set; }
    public OfferingData Offering { get; set; }
}
