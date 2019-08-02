using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        private ExplorerClientProvider _Client;

        public BTCPayWalletProvider(ExplorerClientProvider client,
            BTCPayServerOptions btcPayServerOptions,
            IOptions<MemoryCacheOptions> memoryCacheOption)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;

            foreach(var network in btcPayServerOptions.FilteredNetworks.OfType<BTCPayNetwork>())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode, new BTCPayWallet(explorerClient, new MemoryCache(memoryCacheOption), network));
            }
        }

        Dictionary<string, BTCPayWallet> _Wallets = new Dictionary<string, BTCPayWallet>();

        public BTCPayWallet GetWallet(BTCPayNetworkBase network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return GetWallet(network.CryptoCode);
        }
        public BTCPayWallet GetWallet(string cryptoCode)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            _Wallets.TryGetValue(cryptoCode, out var result);
            return result;
        }

        public bool IsAvailable(BTCPayNetworkBase network)
        {
            return _Client.IsAvailable(network);
        }

        public IEnumerable<BTCPayWallet> GetWallets()
        {
            foreach (var w in _Wallets)
                yield return w.Value;
        }
    }
}
