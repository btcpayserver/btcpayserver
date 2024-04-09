using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public class EditStoreViewModel
    {
        public string StoreId { get; set; }
        public string Name { get; set; }

        [Required]
        [MaxLength(10)]
        [Display(Name = "Default currency")]
        public string DefaultCurrency { get; set; }

        [Display(Name = "Preferred Price Source")]
        public string PreferredExchange { get; set; }

        public SelectList Exchanges { get; set; }
    }
}
