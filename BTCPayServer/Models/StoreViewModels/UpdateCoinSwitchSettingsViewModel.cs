using BTCPayServer.Payments.CoinSwitch;

namespace BTCPayServer.Models.StoreViewModels
{
    public class UpdateCoinSwitchSettingsViewModel
    {
        public string MerchantId { get; set; }
        public bool Enabled { get; set; }

        public string StatusMessage { get; set; }
    }
}
