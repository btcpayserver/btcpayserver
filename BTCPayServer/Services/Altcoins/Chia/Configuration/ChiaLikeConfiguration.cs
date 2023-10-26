#if ALTCOINS
using System;
using System.Collections.Generic;
using BTCPayServer.Common.Altcoins.Chia.RPC;
using BTCPayServer.Services.Altcoins.Chia.RPC;

namespace BTCPayServer.Services.Altcoins.Chia.Configuration
{
    public class ChiaLikeConfiguration
    {
        public Dictionary<string, ChiaLikeConfigurationItem> ChiaLikeConfigurationItems { get; set; } =
            new Dictionary<string, ChiaLikeConfigurationItem>();
    }

    public class ChiaLikeConfigurationItem
    {
        public EndpointInfo FullNodeEndpoint { get; set; }
        public EndpointInfo WalletEndpoint { get; set; }
    }
}
#endif
