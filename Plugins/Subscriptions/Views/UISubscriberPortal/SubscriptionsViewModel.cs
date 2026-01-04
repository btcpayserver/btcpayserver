using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Emails.Views;

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
    public bool TooMuchSubscribers { get; set; }
    public string Currency { get; set; }

    public int TotalPlans { get; set; }

    public int TotalSubscribers { get; set; }
    public string TotalMonthlyRevenue { get; set; }
    public record SelectablePlan(string Name, string Id, bool HasTrial);
    public List<SelectablePlan> SelectablePlans { get; set; }

    public bool EmailConfigured { get; set; }

    public class EmailRule(EmailRuleData data)
    {
        public EmailTriggerViewModel TriggerViewModel { get; set; }
        public EmailRuleData Data { get; set; } = data;
    }

    public List<EmailRule> EmailRules { get; set; }
    public List<EmailTriggerViewModel> AvailableTriggers { get; set; }
    public int PaymentRemindersDays { get; set; }
    public string SearchTerm { get; set; }
}
