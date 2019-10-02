using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Services.Wallets
{
    public class WalletReceiveStateService
    {
        private readonly ConcurrentDictionary<WalletId, KeyPathInformation> _walletReceiveState =
            new ConcurrentDictionary<WalletId, KeyPathInformation>();

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

        public void Set(WalletId walletId, KeyPathInformation information)
        {
            _walletReceiveState.AddOrReplace(walletId, information);
        }

        public IEnumerable<KeyValuePair<WalletId, KeyPathInformation>> GetByDerivation(
            DerivationStrategyBase derivationStrategyBase)
        {
            return _walletReceiveState.Where(pair => pair.Value.DerivationStrategy == derivationStrategyBase);
        }
    }
}
