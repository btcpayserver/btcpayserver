namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class WalletsQuery
    {
        public string UserId { get; set; }
        public bool IncludeTransactions { get; set; }
    }
}
