#if ALTCOINS
using System;
using System.Collections.Generic;

namespace BTCPayServer.Services.Altcoins.Zcash.Configuration
{
    public class ZcashLikeConfiguration
    {
        public Dictionary<string, ZcashLikeConfigurationItem> ZcashLikeConfigurationItems { get; set; } =
            new Dictionary<string, ZcashLikeConfigurationItem>();
    }

    public class ZcashLikeConfigurationItem
    {
        public Uri DaemonRpcUri { get; set; }
        public Uri InternalWalletRpcUri { get; set; }
        public string WalletDirectory { get; set; }
    }
}
#endif
