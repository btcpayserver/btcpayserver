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
        public uint256 UnconfirmedHash { get; set; }
        public uint256 ConfirmedHash { get; set; }
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

        public BTCPayWallet(ExplorerClient client, BTCPayNetwork network)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
            _Network = network;
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
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            return pathInfo.ScriptPubKey.GetDestinationAddress(_Client.Network);
        }

        public async Task TrackAsync(DerivationStrategyBase derivationStrategy)
        {
            await _Client.TrackAsync(derivationStrategy);
        }

        public Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            return _Client.GetTransactionAsync(txId, cancellation);
        }

        public async Task<NetworkCoins> GetCoins(DerivationStrategyBase strategy, KnownState state, CancellationToken cancellation = default(CancellationToken))
        {
            var changes = await _Client.SyncAsync(strategy, state?.ConfirmedHash, state?.UnconfirmedHash, true, cancellation).ConfigureAwait(false);
            return new NetworkCoins()
            {
                TimestampedCoins = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).Select(c => new NetworkCoins.TimestampedCoin() { Coin = c.AsCoin(), DateTime = c.Timestamp }).ToArray(),
                State = new KnownState() { ConfirmedHash = changes.Confirmed.Hash, UnconfirmedHash = changes.Unconfirmed.Hash },
                Strategy = strategy,
                Wallet = this
            };
        }

        public Task BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }


        public async Task<Money> GetBalance(DerivationStrategyBase derivationStrategy)
        {
            var result = await _Client.SyncAsync(derivationStrategy, null, true);
            return result.Confirmed.UTXOs.Select(u => u.Value)
                         .Concat(result.Unconfirmed.UTXOs.Select(u => u.Value))
                         .Sum();
        }
    }
}
