namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreDerivationScheme
    {
        public string Crypto { get; set; }
        public string Value { get; set; }
        public WalletId WalletId { get; set; }
        public bool WalletSupported { get; set; }
        public bool Enabled { get; set; }
        public bool Collapsed { get; set; }
    }
}
