#if ALTCOINS
using System;
using System.Collections.Generic;

namespace BTCPayServer.Services.Altcoins.Monero.Configuration
{
    public class MoneroLikeConfiguration
    {
        public Dictionary<string, MoneroLikeConfigurationItem> MoneroLikeConfigurationItems { get; set; } =
            new Dictionary<string, MoneroLikeConfigurationItem>();
    }

    public class MoneroLikeConfigurationItem
    {
        public Uri DaemonRpcUri { get; set; }
        public Uri InternalWalletRpcUri { get; set; }
        public string WalletDirectory { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
#endif
