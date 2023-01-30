using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Wallets
{
    public class WalletReceiveService : IHostedService
    {
        private readonly CompositeDisposable _leases = new CompositeDisposable();
        private readonly EventAggregator _eventAggregator;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly StoreRepository _storeRepository;
        private readonly WalletRepository _walletRepository;

        private readonly ConcurrentDictionary<WalletId, KeyPathInformation> _walletReceiveState =
            new ConcurrentDictionary<WalletId, KeyPathInformation>();

        public WalletReceiveService(EventAggregator eventAggregator, ExplorerClientProvider explorerClientProvider,
            BTCPayWalletProvider btcPayWalletProvider, BTCPayNetworkProvider btcPayNetworkProvider,
            StoreRepository storeRepository, WalletRepository walletRepository)
        {
            _eventAggregator = eventAggregator;
            _explorerClientProvider = explorerClientProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _storeRepository = storeRepository;
            _walletRepository = walletRepository;
        }

        public async Task<string> UnReserveAddress(WalletId walletId)
        {
            var kpi = Get(walletId);
            if (kpi is null)
            {
                return null;
            }

            var explorerClient = _explorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            if (explorerClient is null)
            {
                return null;
            }

            await explorerClient.CancelReservationAsync(kpi.DerivationStrategy, new[] { kpi.KeyPath });
            Remove(walletId);
            return kpi.Address.ToString();
        }

        public async Task<KeyPathInformation> GetOrGenerate(WalletId walletId, bool forceGenerate = false)
        {
            var existing = Get(walletId);
            if (existing != null && !forceGenerate)
            {
                return existing;
            }

            var wallet = _btcPayWalletProvider.GetWallet(walletId.CryptoCode);
            var store = await _storeRepository.FindStore(walletId.StoreId);
            var derivationScheme = store?.GetDerivationSchemeSettings(_btcPayNetworkProvider, walletId.CryptoCode);
            if (wallet is null || derivationScheme is null)
            {
                return null;
            }

            var reserve = (await wallet.ReserveAddressAsync(walletId.StoreId, derivationScheme.AccountDerivation, "receive"));
            Set(walletId, reserve);
            return reserve;
        }

        public void Remove(WalletId walletId)
        {
            _walletReceiveState.TryRemove(walletId, out _);
        }

        public KeyPathInformation Get(WalletId walletId)
        {
            if (_walletReceiveState.ContainsKey(walletId))
            {
                return _walletReceiveState[walletId];
            }

            return null;
        }

        private void Set(WalletId walletId, KeyPathInformation information)
        {
            _walletReceiveState.AddOrReplace(walletId, information);
        }

        public IEnumerable<KeyValuePair<WalletId, KeyPathInformation>> GetByDerivation(string cryptoCode,
            DerivationStrategyBase derivationStrategyBase)
        {
            return _walletReceiveState.Where(pair =>
                pair.Key.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCulture) &&
                pair.Value.DerivationStrategy == derivationStrategyBase);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _leases.Add(_eventAggregator.Subscribe<WalletChangedEvent>(evt =>
                Remove(evt.WalletId)));

            _leases.Add(_eventAggregator.Subscribe<NewOnChainTransactionEvent>(evt =>
            {
                var matching = GetByDerivation(evt.CryptoCode, evt.NewTransactionEvent.DerivationStrategy).Where(pair =>
                    evt.NewTransactionEvent.Outputs.Any(output => output.ScriptPubKey == pair.Value.ScriptPubKey));

                foreach (var keyValuePair in matching)
                {
                    Remove(keyValuePair.Key);
                }
            }));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _leases.Dispose();
            return Task.CompletedTask;
        }

        public Tuple<WalletId, KeyPathInformation> GetByScriptPubKey(string cryptoCode, Script script)
        {
            var match = _walletReceiveState.Where(pair =>
               pair.Key.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCulture) &&
               pair.Value.ScriptPubKey == script);
            if (match.Any())
            {
                var f = match.First();
                return new Tuple<WalletId, KeyPathInformation>(f.Key, f.Value);
            }

            return null;
        }
    }
}
