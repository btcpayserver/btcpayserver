namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletImportMethod
    {
        SelectMethod, // needs to be first to cover the null case
        Hardware,
        File,
        Xpub,
        Seed,
        Scan
    }

    public class ImportWalletViewModel : DerivationSchemeViewModel
    {
        public WalletImportMethod Method { get; set; }
        public string StoreId { get; set; }

        public string ViewName =>
            Method switch
            {
                WalletImportMethod.Hardware => "ImportWallet/Hardware",
                WalletImportMethod.Xpub => "ImportWallet/Xpub",
                WalletImportMethod.File => "ImportWallet/File",
                WalletImportMethod.Scan => "ImportWallet/Scan",
                WalletImportMethod.Seed => "ImportWallet/Seed",
                WalletImportMethod.SelectMethod => "ImportWallet",
                _ => "ImportWallet"
            };
    }
}
