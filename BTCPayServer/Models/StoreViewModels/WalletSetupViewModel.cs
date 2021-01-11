namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletSetupMethod
    {
        Import,
        Hardware,
        File,
        Xpub,
        Scan,
        Seed,
        Generate
    }

    public class WalletSetupViewModel : DerivationSchemeViewModel
    {
        public WalletSetupMethod? Method { get; set; }
        public WalletSetupRequest SetupRequest { get; set; }
        public string StoreId { get; set; }

        public string ViewName =>
            Method switch
            {
                WalletSetupMethod.Import => "ImportWallet",
                WalletSetupMethod.Hardware => "ImportWallet/Hardware",
                WalletSetupMethod.Xpub => "ImportWallet/Xpub",
                WalletSetupMethod.File => "ImportWallet/File",
                WalletSetupMethod.Scan => "ImportWallet/Scan",
                WalletSetupMethod.Seed => "ImportWallet/Seed",
                WalletSetupMethod.Generate => "GenerateWallet",
                _ => "SetupWallet"
            };
    }
}
