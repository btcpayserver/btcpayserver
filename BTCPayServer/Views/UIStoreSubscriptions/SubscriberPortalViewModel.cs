using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Models;

namespace BTCPayServer.Views.UIStoreMembership;

public class SubscriberPortalViewModel
{
    public SubscriberPortalViewModel()
    {

    }

    public SubscriberPortalViewModel(PortalSessionData data)
    {
        Data = data;
    }

    public StoreBrandingViewModel StoreBranding { get; set; }
    public string StoreName { get; set; }

    public PortalSessionData Data { get; set; }
}
