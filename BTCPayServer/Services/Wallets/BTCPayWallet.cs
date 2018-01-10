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

namespace BTCPayServer.Services.Wallets
{
    public class KnownState
    {
        public uint256 UnconfirmedHash { get; set; }
        public uint256 ConfirmedHash { get; set; }
    }
    public class NetworkCoins
    {
        public Coin[] Coins { get; set; }
        public KnownState State { get; set; }
        public DerivationStrategy Strategy { get; set; }
    }
    public class BTCPayWallet
    {
        private ExplorerClientProvider _Client;

        public BTCPayWallet(ExplorerClientProvider client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            _Client = client;
        }


        public async Task<BitcoinAddress> ReserveAddressAsync(DerivationStrategy derivationStrategy)
        {
            var client = _Client.GetExplorerClient(derivationStrategy.Network);
            var pathInfo = await client.GetUnusedAsync(derivationStrategy.DerivationStrategyBase, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            return pathInfo.ScriptPubKey.GetDestinationAddress(client.Network);
        }

        public async Task TrackAsync(DerivationStrategy derivationStrategy)
        {
            var client = _Client.GetExplorerClient(derivationStrategy.Network);
            await client.TrackAsync(derivationStrategy.DerivationStrategyBase);
        }

        public Task<TransactionResult> GetTransactionAsync(BTCPayNetwork network, uint256 txId, CancellationToken cancellation = default(CancellationToken))
        {
            var client = _Client.GetExplorerClient(network);
            return client.GetTransactionAsync(txId, cancellation);
        }

        public async Task<NetworkCoins> GetCoins(DerivationStrategy strategy, KnownState state, CancellationToken cancellation = default(CancellationToken))
        {
            var client = _Client.GetExplorerClient(strategy.Network);
            if (client == null)
                return new NetworkCoins() { Coins = new Coin[0], State = null, Strategy = strategy };
            var changes = await client.SyncAsync(strategy.DerivationStrategyBase, state?.ConfirmedHash, state?.UnconfirmedHash, true, cancellation).ConfigureAwait(false);
            var utxos = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).Select(c => c.AsCoin()).ToArray();
            return new NetworkCoins()
            {
                Coins = utxos,
                State = new KnownState() { ConfirmedHash = changes.Confirmed.Hash, UnconfirmedHash = changes.Unconfirmed.Hash },
                Strategy = strategy,
            };
        }

        public Task BroadcastTransactionsAsync(BTCPayNetwork network, List<Transaction> transactions)
        {
            var client = _Client.GetExplorerClient(network);
            var tasks = transactions.Select(t => client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }


        public async Task<Money> GetBalance(DerivationStrategy derivationStrategy)
        {
            var client = _Client.GetExplorerClient(derivationStrategy.Network);
            var result = await client.SyncAsync(derivationStrategy.DerivationStrategyBase, null, true);
            return result.Confirmed.UTXOs.Select(u => u.Value)
                         .Concat(result.Unconfirmed.UTXOs.Select(u => u.Value))
                         .Sum();
        }
    }
}
