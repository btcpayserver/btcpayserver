using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CreateStoreViewModel
    {
        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        public string Name { get; set; }

        [Required]
        [MaxLength(10)]
        [Display(Name = "Default currency")]
        public string DefaultCurrency { get; set; }

        [Required]
        [Display(Name = "Preferred Price Source")]
        public string PreferredExchange { get; set; }
        
        public SelectList Exchanges { get; set; }
    }
}
