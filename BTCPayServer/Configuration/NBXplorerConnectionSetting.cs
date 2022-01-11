using System;

namespace BTCPayServer.Configuration
{
    public class NBXplorerConnectionSetting
    {
        public string CryptoCode { get; internal set; }
        public Uri ExplorerUri { get; internal set; }
        public string CookieFile { get; internal set; }
    }
}
