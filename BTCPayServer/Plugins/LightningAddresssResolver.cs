using LNURL;

namespace BTCPayServer.Plugins;

public class LightningAddressResolver
{
    public string Username { get; set; }
    public LNURLPayRequest LNURLPayRequest { get; set; }

    public LightningAddressResolver(string username)
    {
        Username = username;
    }
}
