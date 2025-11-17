using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.Monetization.Views;

public class MonetizationViewModel
{
    public MonetizationSettings Settings { get; set; }

    public PlanData DefaultPlan { get; set; }
    public ActivateMonetizationModelViewModel ActivateModal { get; set; }
    public MigrateUsersModalViewModel MigrateUsersModal { get; set; }
}

public class ActivateMonetizationModelViewModel
{
    public ActivateMonetizationModelViewModel()
    {

    }

    public ActivateMonetizationModelViewModel(
        MonetizationSettings settings,
        List<OfferingData> offerings,
        string selectedStoreId,
        IEnumerable<StoreData> stores) : this()
    {
        Offerings = offerings
            .Select(o => new Offering
                {
                    Name = o.App.Name,
                    Id = o.Id,
                    Plans = o.Plans
                        .Where(p => p.Status == PlanData.PlanStatus.Active)
                        .Select(p => new Plan
                    {
                        Name = p.Name,
                        Id = p.Id
                    }).ToList()
                })
            .Where(o => o.Plans.Any())
            .ToList();
        var selectedOffering = Offerings.FirstOrDefault(o => o.Id == settings?.OfferingId);
        selectedOffering ??= Offerings.FirstOrDefault();
        SelectedOfferingId = selectedOffering?.Id;
        var selectedPlan = selectedOffering?.Plans.FirstOrDefault(p => p.Id == settings?.DefaultPlanId);
        selectedPlan ??= selectedOffering?.Plans.FirstOrDefault();
        SelectedPlanId = selectedPlan?.Id;
        SelectedStoreId = selectedStoreId;
        Stores = stores.Select(s => new SelectListItem(s.StoreName, s.Id)).ToList();
    }

    public class Offering
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public List<Plan> Plans { get; set; }
    }

    public class Plan
    {
        public string Name { get; set; }
        public string Id { get; set; }
    }

    public string SelectedOfferingId { get; set; }
    public string SelectedPlanId { get; set; }
    public List<Offering> Offerings { get; set; }
    public string SelectedStoreId { get; set; }
    public IEnumerable<SelectListItem> Stores { get; set; }

    [Range(0.01, double.MaxValue)]
    [DisplayFormat(DataFormatString = "{0:0.00####}", ApplyFormatInEditMode = true)]
    public decimal StarterPlanCost { get; set; } = 10m;

    [Display(Name = "Trial Period (days)")]
    [Range(0, 3650)]
    public int TrialDays { get; set; } = 7;

    [Display(Name = "Migrate existing non-admin users")]
    public bool MigrateExistingUsers { get; set; }
}
