﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class UpdateChangellySettingsViewModel
    {
        [Required]
        public string ApiKey { get; set; }
        
        [Required]
        public string ApiSecret { get; set; }
        
        [Required]
        public string ApiUrl { get; set; } = "https://api.changelly.com";
        
        [Display(Name="Optional, Changelly Merchant Id")]
        public string ChangellyMerchantId { get; set; } = "804298eb5753";
        
        public bool Enabled { get; set; } = true;
        
        public string StatusMessage { get; set; }
        
        [Required]
        [Range(0, 100)]
        [Display(Name = "Percentage to multiply amount requested at Changelly to avoid underpaid situations due to Changelly not guaranteeing rates. ")]
        public decimal AmountMarkupPercentage { get; set; } = new decimal(2);
        
        

    }
}
