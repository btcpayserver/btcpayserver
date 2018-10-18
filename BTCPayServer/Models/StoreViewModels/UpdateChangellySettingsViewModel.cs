using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class UpdateChangellySettingsViewModel
    {
        [Required] public string ApiKey { get; set; } = "6ed02cdf1b614d89a8c0ceb170eebb61";

        [Required] public string ApiSecret { get; set; } = "8fbd66a2af5fd15a6b5f8ed0159c5842e32a18538521ffa145bd6c9e124d3483";

        [Required] public string ApiUrl { get; set; } = "https://api.changelly.com";

        [Display(Name = "Optional, Changelly Merchant Id")]
        public string ChangellyMerchantId { get; set; } = "804298eb5753";

        [Display(Name = "Show Fiat Currencies as option in conversion")]
        public bool ShowFiat { get; set; } = true;

        [Required]
        [Range(0, 100)]
        [Display(Name =
            "Percentage to multiply amount requested at Changelly to avoid underpaid situations due to Changelly not guaranteeing rates. ")]
        public decimal AmountMarkupPercentage { get; set; } = new decimal(2);

        public bool Enabled { get; set; } = true;

        public string StatusMessage { get; set; }
    }
}
