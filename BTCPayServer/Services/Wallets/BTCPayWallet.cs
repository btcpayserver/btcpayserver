using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using System.Threading;
using NBXplorer.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Services.Wallets
{
    public class KnownState
    {
        public UTXOChanges PreviousCall { get; set; }
    }
    public class NetworkCoins
    {
        public class TimestampedCoin
        {
            public DateTimeOffset DateTime { get; set; }
            public Coin Coin { get; set; }
        }
        public TimestampedCoin[] TimestampedCoins { get; set; }
        public KnownState State { get; set; }
        public DerivationStrategyBase Strategy { get; set; }
        public BTCPayWallet Wallet { get; set; }
    }
    public class BTCPayWallet
    {
        private ExplorerClient _Client;
        private TransactionCache _Cache;
        public BTCPayWallet(ExplorerClient client, TransactionCache cache, BTCPayNetwork network)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _Network = network;
            _Cache = cache;
        }


        private readonly BTCPayNetwork _Network;
        public BTCPayNetwork Network
        {
            get
            {
                return _Network;
            }
        }

        public TimeSpan CacheSpan { get; private set; } = TimeSpan.FromMinutes(60);

        public async Task<BitcoinAddress> ReserveAddressAsync(DerivationStrategyBase derivationStrategy)
        {
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            // Might happen on some broken install
            if (pathInfo == null)
            {
                await _Client.TrackAsync(derivationStrategy).ConfigureAwait(false);
                pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            }
            return pathInfo.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork);
        }

        public async Task<(BitcoinAddress, KeyPath)> GetChangeAddressAsync(DerivationStrategyBase derivationStrategy)
        {
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Change, 0, false).ConfigureAwait(false);
            // Might happen on some broken install
            if (pathInfo == null)
            {
                await _Client.TrackAsync(derivationStrategy).ConfigureAwait(false);
                pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Change, 0, false).ConfigureAwait(false);
            }
            return (pathInfo.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork), pathInfo.KeyPath);
        }

        public async Task TrackAsync(DerivationStrategyBase derivationStrategy)
        {
            await _Client.TrackAsync(derivationStrategy);
        }

        public async Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            var tx = _Cache.GetTransaction(txId);
            if (tx != null)
                return tx;
            tx = await _Client.GetTransactionAsync(txId, cancellation);
            _Cache.AddToCache(tx);
            return tx;
        }

        public async Task<NetworkCoins> GetCoins(DerivationStrategyBase strategy, KnownState state, CancellationToken cancellation = default(CancellationToken))
        {
            var changes = await _Client.GetUTXOsAsync(strategy, state?.PreviousCall, false, cancellation).ConfigureAwait(false);
            return new NetworkCoins()
            {
                TimestampedCoins = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).Select(c => new NetworkCoins.TimestampedCoin() { Coin = c.AsCoin(), DateTime = c.Timestamp }).ToArray(),
                State = new KnownState() { PreviousCall = changes },
                Strategy = strategy,
                Wallet = this
            };
        }

        public Task<BroadcastResult[]> BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }

        public async Task<(Coin[], Dictionary<Script, KeyPath>)> GetUnspentCoins(DerivationStrategyBase derivationStrategy, CancellationToken cancellation = default(CancellationToken))
        {
            var changes = await _Client.GetUTXOsAsync(derivationStrategy, null, false, cancellation).ConfigureAwait(false);
            var keyPaths = new Dictionary<Script, KeyPath>();
            foreach (var coin in changes.GetUnspentUTXOs())
            {
                keyPaths.TryAdd(coin.ScriptPubKey, coin.KeyPath);
            }
            return (changes.GetUnspentCoins(), keyPaths);
        }

        public async Task<Money> GetBalance(DerivationStrategyBase derivationStrategy)
        {
            var result = await _Client.GetUTXOsAsync(derivationStrategy, null, true);
            return result.GetUnspentUTXOs().Select(c => c.Value).Sum();
        }
    }
}
