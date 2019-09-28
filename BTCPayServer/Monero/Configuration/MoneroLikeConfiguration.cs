﻿using System;
using System.Collections.Generic;

namespace BTCPayServer.Monero.Configuration
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
    }
}
