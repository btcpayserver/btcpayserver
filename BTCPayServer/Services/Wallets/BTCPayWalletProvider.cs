using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayOnChainWalletManagerProvider
    {
        private ExplorerClientProvider _Client;
        IOptions<MemoryCacheOptions> _Options;
        public BTCPayOnChainWalletManagerProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    Data.ApplicationDbContextFactory dbContextFactory,
                                    BTCPayNetworkProvider networkProvider)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _Options = memoryCacheOption;

            foreach(var network in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode.ToUpperInvariant(), new BTCPayOnChainWalletManager(explorerClient, new MemoryCache(_Options), network, dbContextFactory));
            }
        }

        Dictionary<string, BTCPayOnChainWalletManager> _Wallets = new Dictionary<string, BTCPayOnChainWalletManager>();

        public BTCPayOnChainWalletManager GetWallet(BTCPayNetworkBase network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return GetWallet(network.CryptoCode);
        }
        public BTCPayOnChainWalletManager GetWallet(string cryptoCode)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            _Wallets.TryGetValue(cryptoCode.ToUpperInvariant(), out var result);
            return result;
        }

        public bool IsAvailable(BTCPayNetworkBase network)
        {
            return _Client.IsAvailable(network);
        }

        public IEnumerable<BTCPayOnChainWalletManager> GetWallets()
        {
            foreach (var w in _Wallets)
                yield return w.Value;
        }
    }
}
