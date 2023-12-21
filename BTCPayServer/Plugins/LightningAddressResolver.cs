using LNURL;

namespace BTCPayServer.Plugins;

public class LightningAddressResolver(string username)
{
    public string Username { get; set; } = username?.ToLowerInvariant();
    public LNURLPayRequest LNURLPayRequest { get; set; }
}
