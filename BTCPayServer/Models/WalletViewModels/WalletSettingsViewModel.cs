using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSettingsViewModel
    {
        public string Label { get; set; }
        [DisplayName("Derivation scheme")]
        public string DerivationScheme { get; set; }
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
        [DisplayName("Account key")]
        public string AccountKey { get; set; }
        [DisplayName("Master fingerprint")]
        [Validation.HDFingerPrintValidator]
        public string MasterFingerprint { get; set; }
        [DisplayName("Account key path")]
        [Validation.KeyPathValidator]
        public string AccountKeyPath { get; set; }
    }
}
