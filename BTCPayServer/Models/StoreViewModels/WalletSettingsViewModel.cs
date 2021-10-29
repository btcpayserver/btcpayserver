using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class WalletSettingsViewModel : DerivationSchemeViewModel
    {
        public string StoreId { get; set; }
        public bool IsHotWallet { get; set; }
        public bool CanUsePayJoin { get; set; }
        
        [Display(Name = "Enable Payjoin/P2EP")]
        public bool PayJoinEnabled { get; set; }

        [Display(Name = "Show recommended fee")]
        public bool ShowRecommendedFee { get; set; }

        [Display(Name = "Recommended fee confirmation target blocks")]
        [Range(1, double.PositiveInfinity)]
        public int RecommendedFeeBlockTarget { get; set; }

        [Display(Name = "Payment invalid if transactions fails to confirm … after invoice expiration")]
        [Range(10, 60 * 24 * 24)]
        public int MonitoringExpiration { get; set; }

        [Display(Name = "Consider the invoice confirmed when the payment transaction …")]
        public SpeedPolicy SpeedPolicy { get; set; }
    }
}
