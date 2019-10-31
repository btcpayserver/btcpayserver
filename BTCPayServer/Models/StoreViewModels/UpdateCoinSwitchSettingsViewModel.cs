using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Payments.CoinSwitch;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class UpdateCoinSwitchSettingsViewModel
    {
        public string MerchantId { get; set; }
        public bool Enabled { get; set; }

        [Display(Name = "Integration Mode")]
        public string Mode { get; set; } = "inline";
        
        [Required]
        [Range(0, 100)]
        [Display(Name =
            "Percentage to multiply amount requested at Coinswitch to avoid underpaid situations due to Coinswitch not guaranteeing rates. ")]
        public decimal AmountMarkupPercentage { get; set; } = new decimal(2);

        public List<SelectListItem> Modes { get; } = new List<SelectListItem>
        {
            new SelectListItem { Value = "popup", Text = "Open in a popup" },
            new SelectListItem { Value = "inline", Text = "Embed inside Checkout UI " },
        };
    }
}
