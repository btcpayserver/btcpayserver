using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.WalletViewModels
{
    public class SignWithSeedViewModel
    {
        [Required]
        public string PSBT { get; set; }
        [Required][Display(Name = "Seed(12/24 word mnemonic seed) Or HD private key(xprv...)")]
        public string SeedOrKey { get; set; }

        [Display(Name = "Optional seed passphrase")]
        public string Passphrase { get; set; }

        public bool Send { get; set; }
    }
}
