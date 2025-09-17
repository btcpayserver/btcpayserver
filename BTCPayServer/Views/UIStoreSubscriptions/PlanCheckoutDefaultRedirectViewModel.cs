using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Models;

namespace BTCPayServer.Views.UIStoreMembership;

public class PlanCheckoutDefaultRedirectViewModel
{
    public PlanCheckoutDefaultRedirectViewModel()
    {

    }

    public PlanCheckoutDefaultRedirectViewModel(PlanCheckoutData data)
    {
        Data = data;
        Id = data.Id;
    }
    public string Title { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
    public string Id { get; set; }
    public PlanCheckoutData Data { get; set; }
}
