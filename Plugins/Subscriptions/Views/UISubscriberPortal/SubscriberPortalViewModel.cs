using System;
using System.Collections.Generic;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Subscriptions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BTCPayServer.Views.UIStoreMembership;

public class SubscriberPortalViewModel
{
    public class CreditViewModel
    {
        public string Currency { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CreditApplied { get; set; }
        public decimal NextCharge { get; set; }
        public decimal? InputAmount { get; set; }
    }

    public SubscriberPortalViewModel()
    {
    }

    public SubscriberPortalViewModel(PortalSessionData data)
    {
        Data = data;
        var credit = data.Subscriber.GetCredit();
        var applied = Math.Min(credit, data.Subscriber.Plan.Price);
        var nextCharge = data.Subscriber.Plan.Price - applied;
        Credit = new()
        {
            Currency = data.Subscriber.Plan.Currency,
            CurrentBalance = credit,
            CreditApplied = applied,
            InputAmount = nextCharge > 0 ? nextCharge : null,
            NextCharge = nextCharge
        };
    }

    public StoreBrandingViewModel StoreBranding { get; set; }
    public string StoreName { get; set; }


    public PortalSessionData Data { get; set; }
    public (decimal Value, string Display) Refund { get; set; }
    public CreditViewModel Credit { get; set; }

    public record BalanceTransactionViewModel(DateTimeOffset Date, HtmlString Description, decimal Amount, decimal TotalBalance);

    public List<BalanceTransactionViewModel> Transactions { get; set; } = new();
    public List<PlanChange> PlanChanges { get; set; }

    public class PlanChange
    {
        public PlanChange(PlanData plan)
        {
            Name = plan.Name;
            PlanId = plan.Id;
            RecurringType = plan.RecurringType;
            Currency = plan.Currency;
            Price = plan.Price;
        }

        public string Name { get; set; }
        public bool Current { get; set; }
        public PlanChangeData.ChangeType ChangeType { get; set; }
        public string PlanId { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; }
        public PlanData.RecurringInterval RecurringType { get; set; }
    }

    public Dictionary<string, MigratePopup> MigratePopups { get; set; }
    public class MigratePopup
    {
        public string Cost { get; set; }
        public string UsedCredit { get; set; }
        public string AmountDue { get; set; }
        public (string Text, decimal Value) CreditBalanceAdjustment { get; set; }
    }

    [BindingBehavior(BindingBehavior.Never)]
    [ValidateNever]
    public SubscriberData Subscriber => Data.Subscriber;

    [BindingBehavior(BindingBehavior.Never)]
    [ValidateNever]
    public PlanData Plan => Data.Subscriber.Plan;

    public string Anchor { get; set; }
    public string Url { get; set; }
    public string Logo => StoreBranding?.LogoUrl ?? BTCPayLogo;
    public string BTCPayLogo { get; set; }
}
