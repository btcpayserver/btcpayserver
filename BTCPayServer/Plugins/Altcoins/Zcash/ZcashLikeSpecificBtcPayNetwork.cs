namespace BTCPayServer.Plugins.Altcoins;

public class ZcashLikeSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation = 10;
    public string UriScheme { get; set; }
}
