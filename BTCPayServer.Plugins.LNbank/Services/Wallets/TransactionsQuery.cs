namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class TransactionsQuery
    {
        public string UserId { get; set; }
        public string WalletId { get; set; }
        public bool IncludingExpired { get; set; }
        public bool IncludingPending { get; set; } = true;
        public bool IncludingPaid { get; set; } = true;
        public bool IncludeWallet { get; set; }
    }
}
