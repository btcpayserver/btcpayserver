using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        public bool Enabled { get; set; } = true;

        public IEnumerable<DerivationStrategy> AvailableTargetPaymentMethods { get; set; }
        
        public string StatusMessage { get; set; }
    }
}
