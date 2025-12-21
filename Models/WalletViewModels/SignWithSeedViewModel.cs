using System;
using System.ComponentModel.DataAnnotations;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class SignWithSeedViewModel : IHasBackAndReturnUrl
    {
        public SigningContextModel SigningContext { get; set; } = new SigningContextModel();

        [Required]
        [Display(Name = "BIP39 Seed (12/24 word mnemonic phrase) or HD private key (xprv...)")]
        public string SeedOrKey { get; set; }

        [Display(Name = "Optional seed passphrase")]
        public string Passphrase { get; set; }

        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }

        public ExtKey GetExtKey(Network network)
        {
            ExtKey extKey = null;
            try
            {
                var mnemonic = new Mnemonic(SeedOrKey);
                extKey = mnemonic.DeriveExtKey(Passphrase);
            }
            catch (Exception)
            {
            }

            if (extKey == null)
            {
                try
                {
                    extKey = ExtKey.Parse(SeedOrKey, network);
                }
                catch (Exception)
                {
                }
            }
            return extKey;
        }
    }
}
