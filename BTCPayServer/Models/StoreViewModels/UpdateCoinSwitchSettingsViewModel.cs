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

        public List<SelectListItem> Modes { get; } = new List<SelectListItem>
        {
            new SelectListItem { Value = "popup", Text = "Open in a popup" },
            new SelectListItem { Value = "inline", Text = "Embed inside Checkout UI " },
        };
        
        public string StatusMessage { get; set; }
    }
}
