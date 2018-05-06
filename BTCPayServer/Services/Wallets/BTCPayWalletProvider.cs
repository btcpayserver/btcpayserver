using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Ethereum;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        private ExplorerClientProvider _Client;
        BTCPayNetworkProvider _NetworkProvider;
        IOptions<MemoryCacheOptions> _Options;
        private readonly Web3Provider _web3Provider;

        public BTCPayWalletProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    Web3Provider web3Provider,
                                    BTCPayNetworkProvider networkProvider)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _NetworkProvider = networkProvider;
            _Options = memoryCacheOption;
            _web3Provider = web3Provider;

            foreach (var network in networkProvider.GetAll())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient != null)
                {
                    _Wallets.Add(network.CryptoCode,
                        new BTCPayWallet(explorerClient, new MemoryCache(_Options), network));
                    continue;
                }

                var web3 = _web3Provider.GetWeb3(network);
                if (web3 != null)
                {
                    _Wallets.Add(network.CryptoCode,
                        new EthereumPayWallet(web3, new MemoryCache(_Options), network));
                }

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
            return _Client.IsAvailable(network) || _web3Provider.IsAvailable(network);
        }

        public IEnumerable<BTCPayWallet> GetWallets()
        {
            foreach (var w in _Wallets)
                yield return w.Value;
        }
    }
}
