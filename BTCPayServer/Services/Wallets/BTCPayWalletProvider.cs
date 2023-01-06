using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        public WalletRepository WalletRepository { get; }
        public Logs Logs { get; }

        private readonly ExplorerClientProvider _Client;
        readonly BTCPayNetworkProvider _NetworkProvider;
        readonly IOptions<MemoryCacheOptions> _Options;
        public BTCPayWalletProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    Data.ApplicationDbContextFactory dbContextFactory,
                                    BTCPayNetworkProvider networkProvider,
                                    NBXplorerConnectionFactory nbxplorerConnectionFactory,
                                    WalletRepository walletRepository,
                                    Logs logs)
        {
            ArgumentNullException.ThrowIfNull(client);
            this.Logs = logs;
            _Client = client;
            _NetworkProvider = networkProvider;
            WalletRepository = walletRepository;
            _Options = memoryCacheOption;

            foreach (var network in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode.ToUpperInvariant(), new BTCPayWallet(explorerClient, new MemoryCache(_Options), network, WalletRepository, dbContextFactory, nbxplorerConnectionFactory, Logs));
            }
        }

        readonly Dictionary<string, BTCPayWallet> _Wallets = new Dictionary<string, BTCPayWallet>();

        public BTCPayWallet GetWallet(BTCPayNetworkBase network)
        {
            ArgumentNullException.ThrowIfNull(network);
            return GetWallet(network.CryptoCode);
        }
        public BTCPayWallet GetWallet(string cryptoCode)
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
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
