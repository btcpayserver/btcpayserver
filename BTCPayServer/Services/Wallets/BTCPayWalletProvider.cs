using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        private ExplorerClientProvider _Client;
        BTCPayNetworkProvider _NetworkProvider;
        IOptions<MemoryCacheOptions> _Options;
        public BTCPayWalletProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    BTCPayNetworkProvider networkProvider)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _NetworkProvider = networkProvider;
            _Options = memoryCacheOption;

            foreach(var network in networkProvider.GetAll())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode, new BTCPayWallet(explorerClient, new MemoryCache(_Options), network));
            }
        }

        Dictionary<string, BTCPayWallet> _Wallets = new Dictionary<string, BTCPayWallet>();

        public BTCPayWallet GetWallet(BTCPayNetwork network)
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

        public bool IsAvailable(BTCPayNetwork network)
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
