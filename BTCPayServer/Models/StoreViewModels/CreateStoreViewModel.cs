using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CreateStoreViewModel
    {
        public bool IsFirstStore { get; set; }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "Name")]
        public string Name { get; set; }

        [Required]
        [MaxLength(CurrencyNameTable.MaxCurrencyCodeLength)]
        [Display(Name = "Default currency")]
        public string DefaultCurrency { get; set; }

        [Display(Name = "Preferred Price Source")]
        public string PreferredExchange { get; set; }

        public SelectList Exchanges { get; set; }
        public bool CanEditPreferredExchange { get; set; }
    }
}
