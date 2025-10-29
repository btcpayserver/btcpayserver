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
    public enum InstallStatus
    {
        SetOffering,
        ConfigureServerEmail,
        ConfigureStoreEmail,
        Done
    }

    public bool EmailServerConfigured { get; set; }
    public bool EmailStoreConfigured { get; set; }
    public InstallStatus Step { get; set; }
    public MonetizationSettings Settings { get; set; }

    public PlanData DefaultPlan { get; set; }
    public ActivateMonetizationModelViewModel ActivateModal { get; set; }
    public MigrateUsersModalViewModel MigrateUsersModal { get; set; }
    public OfferingData Offering { get; set; }
    public SelectExistingOfferingModalViewModel SelectExistingOfferingModal { get; set; }
}

public class ActivateMonetizationModelViewModel
{
    public ActivateMonetizationModelViewModel()
    {

    }

    public ActivateMonetizationModelViewModel(
        string selectedStoreId,
        IEnumerable<StoreData> stores) : this()
    {
        SelectedStoreId = selectedStoreId;
        Stores = stores.Select(s => new SelectListItem(s.StoreName, s.Id)).ToList();
    }
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
