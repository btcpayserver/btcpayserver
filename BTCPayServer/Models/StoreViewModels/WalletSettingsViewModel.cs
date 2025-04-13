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

        [Display(Name = "Enabled")]
        public bool Enabled { get; set; }
        public bool CanUsePayJoin { get; set; }

        [Display(Name = "Enable Payjoin/P2EP")]
        public bool PayJoinEnabled { get; set; }

        [Display(Name = "Label")]
        public string Label { get; set; }

        public string DerivationSchemeInput { get; set; }
        [Display(Name = "Is signing key")]
        public string SelectedSigningKey { get; set; }
        public bool IsMultiSig => AccountKeys.Count > 1;

        public List<WalletSettingsAccountKeyViewModel> AccountKeys { get; set; } = new();
        public bool NBXSeedAvailable { get; set; }
        public string StoreName { get; set; }
        public string UriScheme { get; set; }
        
        #region MultiSig related settings
        public bool CanSetupMultiSig { get; set; }
        [Display(Name = "Is MultiSig on Server")]
        public bool IsMultiSigOnServer { get; set; }
        
        // some hardware devices like Jade require sending full input transactions if there are multiple inputs
        // https://github.com/Blockstream/Jade/blob/0d6ce77bf23ef2b5dc43cdae3967b4207e8cad52/main/process/sign_tx.c#L586
        [Display(Name = "Default Include NonWitness Utxo in PSBTs")]
        public bool DefaultIncludeNonWitnessUtxo { get; set; }
        #endregion
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
