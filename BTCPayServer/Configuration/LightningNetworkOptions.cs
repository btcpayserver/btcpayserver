using System.Collections.Generic;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration
{
    public class LightningNetworkOptions
    {
        public Dictionary<string, LightningConnectionString> InternalLightningByCryptoCode { get; set; } =
            new Dictionary<string, LightningConnectionString>();
    }
}
