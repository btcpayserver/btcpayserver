namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class WalletQuery
    {
        public string UserId { get; set; }
        public string WalletId { get; set; }
        public bool IncludeTransactions { get; set; }
    }
}
