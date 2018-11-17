using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.AppViewModels
{
    public class UpdatePointOfSaleViewModel
    {
        [Required]
        [MaxLength(30)]
        public string Title { get; set; }
        [Required]
        [MaxLength(5)]
        public string Currency { get; set; }
        [MaxLength(5000)]
        public string Template { get; set; }

        [Display(Name = "User can input custom amount")]
        public bool ShowCustomAmount { get; set; }
        public string Example1 { get; internal set; }
        public string Example2 { get; internal set; }
        public string ExampleCallback { get; internal set; }
        public string InvoiceUrl { get; internal set; }

        [Required]
        [MaxLength(30)]
        [Display(Name = "Text to display on each buttons for items with a specific price")]
        public string ButtonText { get; set; }
        [Required]
        [MaxLength(30)]
        [Display(Name = "Text to display on buttons next to the input allowing the user to enter a custom amount")]
        public string CustomButtonText { get; set; }

        [MaxLength(500)]
        [Display(Name = "Custom bootstrap CSS file")]
        public string CustomCSSLink { get; set; }
    }
}
