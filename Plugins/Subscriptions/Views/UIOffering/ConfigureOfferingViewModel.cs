using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
        foreach (var feature in offeringData.Features.OrderBy(b => b.CustomId))
        {
            Features.Add(new FeatureViewModel()
            {
                Id = feature.CustomId,
                ShortDescription = feature.Description
            });
        }
        Data = offeringData;
    }

    public OfferingData Data { get; set; }
    public class FeatureViewModel
    {
        [StringLength(50)]
        [Required]
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

    public List<FeatureViewModel> Features { get; set; } = new();
    public string Anchor { get; set; }
}
