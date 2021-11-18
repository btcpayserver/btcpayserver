namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class WalletsQuery
    {
        public string[] UserId { get; set; }
        public string[] WalletId { get; set; }
        public string[] AccessKey { get; set; }
        public bool IncludeTransactions { get; set; }
        public bool IncludeAccessKeys { get; set; }
        
    }
}
