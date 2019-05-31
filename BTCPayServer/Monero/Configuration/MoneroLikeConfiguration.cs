using System;
using System.Collections.Generic;

namespace BTCPayServer.Payments.Monero
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
    }
}
