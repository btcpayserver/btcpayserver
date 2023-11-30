namespace BTCPayServer.Plugins.Altcoins;

public class MoneroLikeSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation = 10;
    public string UriScheme { get; set; }
}

