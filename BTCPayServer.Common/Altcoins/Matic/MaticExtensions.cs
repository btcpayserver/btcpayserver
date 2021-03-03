#if ALTCOINS
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer
{
    public static class MaticExtensions
    {
        
        public static IEnumerable<string> GetAllMaticSubChains(this BTCPayNetworkProvider networkProvider, BTCPayNetworkProvider unfiltered)
        {
            var ethBased = networkProvider.GetAll().OfType<MaticBTCPayNetwork>();
            var chainId = ethBased.Select(network => network.ChainId).Distinct();
            return unfiltered.GetAll().OfType<MaticBTCPayNetwork>()
                .Where(network => chainId.Contains(network.ChainId))
                .Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
