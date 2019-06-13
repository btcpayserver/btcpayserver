using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSettingsViewModel
    {
        public string Label { get; set; }
        public string DerivationScheme { get; set; }
        public string DerivationSchemeInput { get; set; }
        [Display(Name = "Is signing key")]
        public string SelectedSigningKey { get; set; }
        public bool IsMultiSig => AccountKeys.Count > 1;

        public List<WalletSettingsAccountKeyViewModel> AccountKeys { get; set; } = new List<WalletSettingsAccountKeyViewModel>();
    }

    public class WalletSettingsAccountKeyViewModel
    {
        public string AccountKey { get; set; }
        [Validation.HDFingerPrintValidator]
        public string MasterFingerprint { get; set; }
        [Validation.KeyPathValidator]
        public string AccountKeyPath { get; set; }
    }
}
