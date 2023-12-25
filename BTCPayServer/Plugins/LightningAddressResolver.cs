using LNURL;

namespace BTCPayServer.Plugins;

public class LightningAddressResolver(string username)
{
    public string Username { get; set; } = LightningAddressService.NormalizeUsername(username);
    public LNURLPayRequest LNURLPayRequest { get; set; }
}
