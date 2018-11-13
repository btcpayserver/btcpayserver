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
    }
}
