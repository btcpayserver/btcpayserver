namespace BTCPayApp.CommonServer
{
    public class PairSuccessResult
    {
        public string Key { get; set; }
        public string? StoreId { get; set; }
        public string UserId { get; set; }

        public string? ExistingWallet { get; set; }
        public string? ExistingWalletSeed { get; set; }
        public string Network { get; set; }
    }
}
