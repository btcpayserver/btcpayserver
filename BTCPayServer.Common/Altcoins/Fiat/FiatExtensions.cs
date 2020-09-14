#if ALTCOINS
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Common.Altcoins.Fiat
{
    public static class FiatExtensions
    {
        public static IEnumerable<string> GetAllFiatChains(this BTCPayNetworkProvider networkProvider)
        {
            return Enumerable.OfType<FiatPayNetwork>(networkProvider.GetAll())
                .Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
