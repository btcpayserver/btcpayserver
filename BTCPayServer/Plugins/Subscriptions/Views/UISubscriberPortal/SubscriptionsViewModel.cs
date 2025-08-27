using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services.Mails;
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
    public record SelectablePlan(string Name, string Id, bool HasTrial);
    public List<SelectablePlan> SelectablePlans { get; set; }

    public bool EnablePaymentReminders { get; set; }
    public bool EmailConfigured { get; set; }
    public OfferingData.MailSettings MailSettings { get; set; }

    public class EmailRule(EmailRuleData data)
    {
        public EmailTriggerViewModel TriggerViewModel { get; set; }
        public EmailRuleData Data { get; set; } = data;
    }

    public List<EmailRule> EmailRules { get; set; }
    public List<EmailTriggerViewModel> NewEmailRules { get; set; }
}
