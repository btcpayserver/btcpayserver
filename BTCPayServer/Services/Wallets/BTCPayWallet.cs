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
    public class GetCoinsResult
    {
        public Coin[] Coins { get; set; }
        public KnownState State { get; set; }
        public DerivationStrategy Strategy { get; set; }
    }
    public class BTCPayWallet
    {
        private ExplorerClient _Client;
        private Serializer _Serializer;
        ApplicationDbContextFactory _DBFactory;

        public BTCPayWallet(ExplorerClient client, ApplicationDbContextFactory factory)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _Client = client;
            _DBFactory = factory;
            _Serializer = new NBXplorer.Serializer(_Client.Network);
            LongPollingMode = client.Network == Network.RegTest;
        }


        public async Task<BitcoinAddress> ReserveAddressAsync(DerivationStrategy derivationStrategy)
        {
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy.DerivationStrategyBase, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            return pathInfo.ScriptPubKey.GetDestinationAddress(_Client.Network);
        }

        public async Task TrackAsync(DerivationStrategy derivationStrategy)
        {
            await _Client.TrackAsync(derivationStrategy.DerivationStrategyBase);
        }

        public Task<TransactionResult> GetTransactionAsync(uint256 txId, CancellationToken cancellation = default(CancellationToken))
        {
            return _Client.GetTransactionAsync(txId, cancellation);
        }

        public bool LongPollingMode { get; set; }
        public async Task<GetCoinsResult> GetCoins(DerivationStrategy strategy, KnownState state, CancellationToken cancellation = default(CancellationToken))
        {
            var changes = await _Client.SyncAsync(strategy.DerivationStrategyBase, state?.ConfirmedHash, state?.UnconfirmedHash, !LongPollingMode, cancellation).ConfigureAwait(false);
            var utxos = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).Select(c => c.AsCoin()).ToArray();
            return new GetCoinsResult()
            {
                Coins = utxos,
                State = new KnownState() { ConfirmedHash = changes.Confirmed.Hash, UnconfirmedHash = changes.Unconfirmed.Hash },
                Strategy = strategy,
            };
        }

        private byte[] ToBytes<T>(T obj)
        {
            return ZipUtils.Zip(_Serializer.ToString(obj));
        }

        public Task BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }

        public async Task<Money> GetBalance(DerivationStrategy derivationStrategy)
        {
            var result = await _Client.SyncAsync(derivationStrategy.DerivationStrategyBase, null, true);
            return result.Confirmed.UTXOs.Select(u => u.Value)
                         .Concat(result.Unconfirmed.UTXOs.Select(u => u.Value))
                         .Sum();
        }
    }
}
