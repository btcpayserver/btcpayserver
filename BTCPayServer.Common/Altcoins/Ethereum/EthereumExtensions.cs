#if ALTCOINS_RELEASE || DEBUG
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer
{
    public static class EthereumExtensions
    {
        
        public static IEnumerable<string> GetAllEthereumSubChains(this BTCPayNetworkProvider networkProvider)
        {
            var ethBased = networkProvider.UnfilteredNetworks.GetAll().OfType<EthereumBTCPayNetwork>();
            var chainId = ethBased.Select(network => network.ChainId).Distinct();
            return networkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Where(network => chainId.Contains(network.ChainId))
                .Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
