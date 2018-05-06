using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Caching.Memory;
using NBXplorer;
using Nethereum.Web3;

namespace BTCPayServer.Payments.Ethereum
{
    public class EthereumPayWallet:BTCPayWallet
    {
        private readonly Web3 _client;

        public EthereumPayWallet(Web3 client, IMemoryCache memoryCache, BTCPayNetwork network) : base(null, memoryCache, network)
        {
            _client = client;
        }
    }
}
