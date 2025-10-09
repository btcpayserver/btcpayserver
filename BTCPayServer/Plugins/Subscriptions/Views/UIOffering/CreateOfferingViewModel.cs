using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Views.UIStoreMembership;

public class CreateOfferingViewModel
{
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = null!;
}
