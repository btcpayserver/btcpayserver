namespace BTCPayServer.Components.WalletNav
{
    public class WalletNavViewModel
    {
        public WalletId WalletId { get; set; }
        public BTCPayNetwork Network { get; set; }
        public string Label { get; set; }
        public string Balance { get; set; }
    }
}
