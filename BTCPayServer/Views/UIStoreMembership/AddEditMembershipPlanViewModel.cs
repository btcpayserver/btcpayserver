using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;

namespace BTCPayServer.Views.UIStoreMembership
{
    public class AddEditMembershipPlanViewModel
    {
        [Required]
        [Display(Name = "Plan Name")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Status")]
        public SubscriptionPlanData.PlanStatus Status { get; set; } = SubscriptionPlanData.PlanStatus.Active;

        [Required]
        [Display(Name = "Price")]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        [Required]
        [Display(Name = "Currency")]
        public string Currency { get; set; }

        [Required]
        [Display(Name = "Recurring Type")]
        public SubscriptionPlanData.RecurringInterval RecurringType { get; set; } = SubscriptionPlanData.RecurringInterval.Monthly;

        [Required]
        [Display(Name = "Grace Period (days)")]
        [Range(0, 3650)]
        public int GracePeriodDays { get; set; }

        [Display(Name = "Description")]
        [StringLength(1000)]
        public string Description { get; set; }

        [Display(Name = "Allow upgrades from other plans")]
        public bool AllowUpgrade { get; set; }

        [Display(Name = "Plan Items")]
        public List<PlanItemInput> Items { get; set; } = new();

        public string Anchor { get; set; }

        public class PlanItemInput
        {
            [Required]
            [Display(Name = "Name")]
            [StringLength(200)]
            public string Name { get; set; } = string.Empty;

            [Display(Name = "ID")]
            [StringLength(100)]
            public string Id { get; set; } = string.Empty;

            [Required]
            [Range(0, int.MaxValue)]
            [Display(Name = "Quantity")]
            public int Quantity { get; set; }

            [Display(Name = "Short description")]
            [StringLength(500)]
            public string ShortDescription { get; set; }
        }
    }
}
