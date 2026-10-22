using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;

namespace BTCPayServer.Views.UIStoreMembership;

public class CreateOfferingViewModel
{
    [Required]
    [StringLength(AppData.MaxNameLength, MinimumLength = AppData.MinNameLength)]
    public string Name { get; set; } = null!;
}
