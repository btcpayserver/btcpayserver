using BTCPayServer.Data.Subscriptions;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Views.UIStoreMembership.Components.SubscriberStatus;

public class SubscriberStatus : ViewComponent
{
    public IViewComponentResult Invoke(SubscriberData subscriber, bool canSuspend = false)
    => View((canSuspend, subscriber));
}
