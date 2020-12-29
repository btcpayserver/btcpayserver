namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletImportMethod
    {
        SelectMethod, // needs to be first to cover the null case
        Hardware,
        File,
        Enter,
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
                WalletImportMethod.Enter => "ImportWallet/Enter",
                WalletImportMethod.File => "ImportWallet/File",
                WalletImportMethod.Scan => "ImportWallet/Scan",
                WalletImportMethod.SelectMethod => "ImportWallet",
                _ => "ImportWallet"
            };
    }
}
