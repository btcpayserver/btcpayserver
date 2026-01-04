using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public class LightningSettingsViewModel : LightningNodeViewModel
    {
        // Payment
        [Display(Name = "Display Lightning payment amounts in Satoshis")]
        public bool LightningAmountInSatoshi { get; set; }

        [Display(Name = "Add hop hints for private channels to the Lightning invoice")]
        public bool LightningPrivateRouteHints { get; set; }

        [Display(Name = "Unify on-chain and lightning payment URL/QR code")]
        public bool OnChainWithLnInvoiceFallback { get; set; }

        [Display(Name = "Description template of the lightning invoice")]
        public string LightningDescriptionTemplate { get; set; }

        // LNURL
        [Display(Name = "Enable LNURL")]
        public bool LNURLEnabled { get; set; }

        [Display(Name = "LNURL Classic Mode")]
        public bool LNURLBech32Mode { get; set; } = true;

        [Display(Name = "Allow payee to pass a comment")]
        public bool LUD12Enabled { get; set; }
    }
}
