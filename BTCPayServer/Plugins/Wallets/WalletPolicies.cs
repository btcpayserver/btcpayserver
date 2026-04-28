namespace BTCPayServer.Plugins.Wallets;

public static class WalletPolicies
{
    public const string CanManageWallets = "btcpay.store.canmanagewallets";
    public const string CanViewWallet = "btcpay.store.canviewwallet";
    public const string CanManageWalletSettings = "btcpay.store.canmanagewalletsettings";
    public const string CanManageWalletTransactions = "btcpay.store.canmanagewallettransactions";
    public const string CanCreateWalletTransactions = "btcpay.store.cancreatetransactions";
    public const string CanSignWalletTransactions = "btcpay.store.cansigntransactions";
    public const string CanBroadcastWalletTransactions = "btcpay.store.canbroadcasttransactions";
    public const string CanCancelWalletTransactions = "btcpay.store.cancanceltransactions";
}
