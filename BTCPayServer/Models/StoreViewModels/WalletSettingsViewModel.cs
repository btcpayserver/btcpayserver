using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
