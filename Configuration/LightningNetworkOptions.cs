using System.Collections.Generic;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration
{
    public class LightningNetworkOptions
    {
        public Dictionary<string, ILightningClient> InternalLightningByCryptoCode { get; set; } = new();
    }
}
