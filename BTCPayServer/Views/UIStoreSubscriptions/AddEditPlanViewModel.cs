using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;

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
        [Display(Name = "Status")]
        public PlanData.PlanStatus Status { get; set; } = PlanData.PlanStatus.Active;

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

        [Display(Name = "Allow upgrades from other plans")]
        public bool AllowUpgrade { get; set; }

        [Display(Name = "Optimistic activation")]
        public bool OptimisticActivation { get; set; } = true;

        [Display(Name = "Entitlements")]
        public List<Entitlement> Entitlements { get; set; } = new();

        public string Anchor { get; set; }

        public class Entitlement
        {
            public string Name { get; set; } = string.Empty;
            public string CustomId { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Quantity")]
            public decimal Quantity { get; set; }

            [Display(Name = "Short description")]
            public string ShortDescription { get; set; }
        }
    }
}
