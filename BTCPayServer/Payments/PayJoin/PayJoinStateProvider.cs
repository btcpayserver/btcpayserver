using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using NBitcoin;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayJoinStateProvider
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;

        private MultiValueDictionary<DerivationStrategyBase, WalletId> Lookup =
            new MultiValueDictionary<DerivationStrategyBase, WalletId>();

        private ConcurrentDictionary<WalletId, PayJoinState> States =
            new ConcurrentDictionary<WalletId, PayJoinState>();

        public PayJoinStateProvider(SettingsRepository settingsRepository, StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider, BTCPayWalletProvider btcPayWalletProvider)
        {
            _settingsRepository = settingsRepository;
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
        }

        public IEnumerable<PayJoinState> Get(string cryptoCode, DerivationStrategyBase derivationStrategyBase)
        {
            if (Lookup.TryGetValue(derivationStrategyBase, out var walletIds))
            {
                var matchedWalletKeys = walletIds.Where(id =>
                    id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase));

                return matchedWalletKeys.Select(id => States.TryGet(id));
            }

            return Array.Empty<PayJoinState>();
        }

        public PayJoinState Get(WalletId walletId)
        {
            return States.TryGet(walletId);
        }

        public ConcurrentDictionary<WalletId, PayJoinState> GetAll()
        {
            return States;
        }

        public PayJoinState GetOrAdd(WalletId key, DerivationStrategyBase derivationStrategyBase,
            IEnumerable<ReceivedCoin> exposedCoins = null)
        {
            return States.GetOrAdd(key, id =>
            {
                Lookup.Add(derivationStrategyBase, id);
                return new PayJoinState(exposedCoins == null
                    ? null
                    : new ConcurrentDictionary<string, ReceivedCoin>(exposedCoins.Select(coin =>
                        new KeyValuePair<string, ReceivedCoin>(coin.OutPoint.ToString(), coin))));
            });
        }

        public void RemoveState(WalletId walletId)
        {
            States.TryRemove(walletId, out _);
        }

        public async Task SaveCoins()
        {
            Dictionary<string, IEnumerable<OutPoint>> saved =
                new Dictionary<string, IEnumerable<OutPoint>>();
            foreach (var payState in GetAll())
            {
                saved.Add(payState.Key.ToString(),
                    payState.Value.GetExposedCoins(true).Select(coin => coin.OutPoint));
            }

            await _settingsRepository.UpdateSetting(saved, "bpu-state");
        }

        public async Task LoadCoins()
        {
            Dictionary<string, IEnumerable<OutPoint>> saved =
                await _settingsRepository.GetSettingAsync<Dictionary<string, IEnumerable<OutPoint>>>("bpu-state");
            if (saved == null)
            {
                return;
            }

            foreach (KeyValuePair<string, IEnumerable<OutPoint>> keyValuePair in saved)
            {
                var walletId = WalletId.Parse(keyValuePair.Key);
                var store = await _storeRepository.FindStore(walletId.StoreId);
                var derivationSchemeSettings = store?.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                    .OfType<DerivationSchemeSettings>().SingleOrDefault(settings =>
                        settings.PaymentId.CryptoCode.Equals(walletId.CryptoCode,
                            StringComparison.InvariantCultureIgnoreCase));
                if (derivationSchemeSettings == null)
                {
                    continue;
                }

                var utxos = await _btcPayWalletProvider.GetWallet(walletId.CryptoCode)
                    .GetUnspentCoins(derivationSchemeSettings.AccountDerivation);

                _ = GetOrAdd(walletId, derivationSchemeSettings.AccountDerivation,
                    utxos.Where(coin => keyValuePair.Value.Contains(coin.OutPoint)));
            }
        }
    }
}
