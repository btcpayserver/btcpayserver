using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
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

    public ActivateMonetizationModelViewModel(MonetizationSettings settings, List<OfferingData> offerings) : this()
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
}
