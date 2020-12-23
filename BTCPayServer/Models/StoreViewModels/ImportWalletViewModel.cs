namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletImportMethod
    {
        SelectMethod,
        Hardware,
        File,
        Enter,
        Scan
    }

    public class ImportWalletViewModel : DerivationSchemeViewModel
    {
        public string StoreId { get; set; }
        public WalletImportMethod Method { get; set; }
    }
}
