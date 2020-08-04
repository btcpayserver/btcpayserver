#if ALTCOINS
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer
{
    public static class LiquidExtensions
    {
        public static IEnumerable<string> GetAllElementsSubChains(this BTCPayNetworkProvider networkProvider, BTCPayNetworkProvider unfilteredNetworkProvider)
        {
            var elementsBased = networkProvider.GetAll().OfType<ElementsBTCPayNetwork>();
            var parentChains = elementsBased.Select(network => network.NetworkCryptoCode.ToUpperInvariant()).Distinct();
            return unfilteredNetworkProvider.GetAll().OfType<ElementsBTCPayNetwork>()
                .Where(network => parentChains.Contains(network.NetworkCryptoCode)).Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
