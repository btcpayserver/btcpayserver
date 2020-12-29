namespace BTCPayServer.Models.StoreViewModels
{
    public class SetupWalletViewModel
    {
        public string StoreId { get; set; }
        public string CryptoCode { get; set; }
        public bool CanUseHotWallet { get; set; }
        public bool CanUseRPCImport { get; set; }
    }
}
