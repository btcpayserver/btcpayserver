using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSettingsViewModel
    {
        public string Label { get; set; }
        public string DerivationScheme { get; set; }
        public string DerivationSchemeInput { get; set; }

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
