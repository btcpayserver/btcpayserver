using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;

namespace BTCPayServer.Models.StoreViewModels
{
    public class GeneralSettingsViewModel
    {
        
        [Display(Name = "Store ID")]
        public string Id { get; set; }
        
        [Display(Name = "Store Name")]
        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        public string StoreName { get; set; }

        [Uri]
        [Display(Name = "Store Website")]
        [MaxLength(500)]
        public string StoreWebsite { get; set; }
        
        public bool CanDelete { get; set; }
    }
}
