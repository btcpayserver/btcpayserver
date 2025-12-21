using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Views.UIStoreMembership
{
    public class AddEditPlanViewModel
    {
        public string OfferingId { get; set; }
        [Required]
        [Display(Name = "Plan Name")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string OfferingName { get; set; }

        [Required]
        [Display(Name = "Price")]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        [Required]
        [Display(Name = "Currency")]
        public string Currency { get; set; }

        [Required]
        [Display(Name = "Recurring Type")]
        public PlanData.RecurringInterval RecurringType { get; set; } = PlanData.RecurringInterval.Monthly;

        [Required]
        [Display(Name = "Grace Period (days)")]
        [Range(0, 3650)]
        public int GracePeriodDays { get; set; }


        [Required]
        [Display(Name = "Trial Period (days)")]
        [Range(0, 3650)]
        public int TrialDays { get; set; }

        [Display(Name = "Description")]
        [StringLength(1000)]
        public string Description { get; set; }

        [Display(Name = "Optimistic activation")]
        public bool OptimisticActivation { get; set; } = true;
        [Display(Name = "Renewable")]
        public bool Renewable { get; set; } = true;

        [Display(Name = "Features")]
        public List<Feature> Features { get; set; } = new();

        public string Anchor { get; set; }
        public string PlanId { get; set; }

        public List<PlanChange> PlanChanges { get; set; } = new();
        public class PlanChange
        {
            public string PlanId { get; set; }
            public string PlanName { get; set; }
            public string SelectedType { get; set; }
        }

        public class Feature
        {
            public string CustomId { get; set; } = string.Empty;
            public string ShortDescription { get; set; } = string.Empty;
            public bool Selected { get; set; }
        }
    }
}
