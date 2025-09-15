using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Validation;

namespace BTCPayServer.Views.UIStoreMembership;

public class ConfigureOfferingViewModel
{
    public ConfigureOfferingViewModel()
    {

    }

    public ConfigureOfferingViewModel(OfferingData offeringData)
    {
        Name = offeringData.App.Name;
        OriginalName = Name;
        SuccessRedirectUrl = offeringData.SuccessRedirectUrl;
        foreach (var entitlement in offeringData.Entitlements)
        {
            Entitlements.Add(new EntitlementViewModel()
            {
                Name = entitlement.Name,
                Id = entitlement.CustomId,
                ShortDescription = entitlement.Description
            });
        }
    }
    public class EntitlementViewModel
    {
        [Required]
        [StringLength(50)]
        public string Name { get; set; }
        [StringLength(50)]
        public string Id { get; set; }
        [StringLength(500)]
        public string ShortDescription { get; set; }
    }

    public string OriginalName { get; set; }
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = null!;

    [Uri]
    [StringLength(500)]
    [Display(Name = "Success redirect url")]
    public string SuccessRedirectUrl { get; set; }

    public List<EntitlementViewModel> Entitlements { get; set; } = new();
    public string Anchor { get; set; }
}
