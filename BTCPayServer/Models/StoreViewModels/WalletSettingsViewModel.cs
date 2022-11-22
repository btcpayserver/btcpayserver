using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Models.StoreViewModels
{
    public class WalletSettingsViewModel : DerivationSchemeViewModel
    {
        public WalletId WalletId { get; set; }
        public string StoreId { get; set; }
        public bool IsHotWallet { get; set; }
        public bool Enabled { get; set; }
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

        [Display(Name = "Consider the invoice settled when the payment transaction …")]
        public SpeedPolicy SpeedPolicy { get; set; }

        public string Label { get; set; }

        public string DerivationSchemeInput { get; set; }
        [Display(Name = "Is signing key")]
        public string SelectedSigningKey { get; set; }
        public bool IsMultiSig => AccountKeys.Count > 1;

        public List<WalletSettingsAccountKeyViewModel> AccountKeys { get; set; } = new List<WalletSettingsAccountKeyViewModel>();
        public bool NBXSeedAvailable { get; set; }
        public string StoreName { get; set; }
        public string UriScheme { get; set; }
    }

    public class WalletSettingsAccountKeyViewModel
    {
        [JsonProperty("ExtPubKey")]
        [Display(Name = "Account key")]
        public string AccountKey { get; set; }
        [Display(Name = "Master fingerprint")]
        [Validation.HDFingerPrintValidator]
        public string MasterFingerprint { get; set; }
        [Display(Name = "Account key path")]
        [Validation.KeyPathValidator]
        public string AccountKeyPath { get; set; }
    }
}
