using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Views.UIStoreMembership
{
    public class AddEditMembershipPlanViewModel
    {
        [Required]
        [Display(Name = "Plan Name")]
        [StringLength(100)]
        public string PlanName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Status")]
        public PlanStatus Status { get; set; } = PlanStatus.Active;

        [Required]
        [Display(Name = "Price")]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        [Required]
        [Display(Name = "Currency")]
        public string Currency { get; set; }

        [Required]
        [Display(Name = "Recurring Type")]
        public RecurringInterval RecurringType { get; set; } = RecurringInterval.Monthly;

        [Required]
        [Display(Name = "Grace Period (days)")]
        [Range(0, 3650)]
        public int GracePeriodDays { get; set; }

        [Display(Name = "Description")]
        [StringLength(1000)]
        public string? Description { get; set; }

        [Display(Name = "Allow upgrades from other plans")]
        public bool AllowUpgrade { get; set; }

        public enum PlanStatus
        {
            Active = 0,
            Inactive = 1,
            Draft = 2
        }

        public enum RecurringInterval
        {
            Monthly = 0,
            Quarterly = 1,
            Yearly = 2
        }
    }
}
