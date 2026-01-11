using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Data.Subscriptions;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.Monetization.Views;

public class MigrateUsersModalViewModel
{
    public string SelectedPlanId { get; set; }
    public List<SelectListItem> AvailablePlans { get; set; }
}
