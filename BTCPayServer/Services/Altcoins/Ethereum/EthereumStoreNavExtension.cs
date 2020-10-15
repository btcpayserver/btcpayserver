#if ALTCOINS
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Ethereum
{
    public class EthereumNavExtension: INavExtension
    {
        public string Partial { get; } = "Ethereum/StoreNavEthereumExtension";
        public string Location { get; } = "store";
    }
}
#endif
