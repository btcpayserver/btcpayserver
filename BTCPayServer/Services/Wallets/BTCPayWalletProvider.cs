using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        private readonly ExplorerClientProvider _Client;
        readonly BTCPayNetworkProvider _NetworkProvider;
        readonly IOptions<MemoryCacheOptions> _Options;
        public BTCPayWalletProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    Data.ApplicationDbContextFactory dbContextFactory,
                                    BTCPayNetworkProvider networkProvider)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _NetworkProvider = networkProvider;
            _Options = memoryCacheOption;

            foreach (var network in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode.ToUpperInvariant(), new BTCPayWallet(explorerClient, new MemoryCache(_Options), network, dbContextFactory));
            }
        }

        readonly Dictionary<string, BTCPayWallet> _Wallets = new Dictionary<string, BTCPayWallet>();

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
            _Wallets.TryGetValue(cryptoCode.ToUpperInvariant(), out var result);
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
