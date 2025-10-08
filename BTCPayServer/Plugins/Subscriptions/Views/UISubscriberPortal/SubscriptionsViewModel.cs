using System;
using System.Collections.Generic;
using System.Globalization;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Views.UIStoreMembership;

public class SubscriptionsViewModel
{
    public SubscriptionsViewModel()
    {

    }

    public SubscriptionsViewModel(OfferingData offeringData)
    {
        Currency = offeringData.App.StoreData.GetStoreBlob().DefaultCurrency;
    }
    public class PlanViewModel
    {
        public PlanData Data { get; set; }
    }

    public class MemberViewModel
    {
        public SubscriberData Data { get; set; }
    }

    public SubscriptionSection Section { get; set; }
    public List<PlanViewModel> Plans { get; set; } = new();
    public List<MemberViewModel> Subscribers { get; set; } = new();
    public string Currency { get; set; }

    public int TotalPlans { get; set; }

    public int TotalSubscribers { get; set; }
    public List<SelectListItem> SelectablePlans { get; set; }
}
