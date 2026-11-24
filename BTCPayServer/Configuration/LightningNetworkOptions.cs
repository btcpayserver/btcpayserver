using System.Collections.Generic;

namespace BTCPayServer.Configuration
{
    public class LightningNetworkOptions
    {
        public Dictionary<string, string> InternalLightningByCryptoCode { get; set; } = new();
    }
}
