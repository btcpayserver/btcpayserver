namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletSetupMethod
    {
        ImportOptions,
        Hardware,
        File,
        Xpub,
        Scan,
        Seed,
        GenerateOptions,
        HotWallet,
        WatchOnly
    }

    public class WalletSetupViewModel : DerivationSchemeViewModel
    {
        public WalletSetupMethod? Method { get; set; }
        public WalletSetupRequest SetupRequest { get; set; }
        public string StoreId { get; set; }
        public bool IsHotWallet { get; set; }

        public string ViewName =>
            Method switch
            {
                WalletSetupMethod.ImportOptions => "ImportWalletOptions",
                WalletSetupMethod.Hardware => "ImportWallet/Hardware",
                WalletSetupMethod.Xpub => "ImportWallet/Xpub",
                WalletSetupMethod.File => "ImportWallet/File",
                WalletSetupMethod.Scan => "ImportWallet/Scan",
                WalletSetupMethod.Seed => "ImportWallet/Seed",
                WalletSetupMethod.GenerateOptions => "GenerateWalletOptions",
                WalletSetupMethod.HotWallet => "GenerateWallet",
                WalletSetupMethod.WatchOnly => "GenerateWallet",
                _ => "SetupWallet"
            };
    }
}
