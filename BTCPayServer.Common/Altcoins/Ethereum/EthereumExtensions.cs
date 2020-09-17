#if ALTCOINS
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer
{
    public static class EthereumExtensions
    {
        
        public static IEnumerable<string> GetAllEthereumSubChains(this BTCPayNetworkProvider networkProvider, BTCPayNetworkProvider unfiltered)
        {
            var ethBased = networkProvider.GetAll().OfType<EthereumBTCPayNetwork>();
            var chainId = ethBased.Select(network => network.ChainId).Distinct();
            return unfiltered.GetAll().OfType<EthereumBTCPayNetwork>()
                .Where(network => chainId.Contains(network.ChainId))
                .Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
