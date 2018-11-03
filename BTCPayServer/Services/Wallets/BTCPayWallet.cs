using NBitcoin;
using Microsoft.Extensions.Logging;
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
using BTCPayServer.Logging;
using System.Collections.Concurrent;

namespace BTCPayServer.Services.Wallets
{
    public class ReceivedCoin
    {
        public Coin Coin { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public KeyPath KeyPath { get; set; }
    }
    public class NetworkCoins
    {
        public class TimestampedCoin
        {
            public DateTimeOffset DateTime { get; set; }
            public Coin Coin { get; set; }
        }
        public TimestampedCoin[] TimestampedCoins { get; set; }
        public DerivationStrategyBase Strategy { get; set; }
        public BTCPayWallet Wallet { get; set; }
    }
    public class BTCPayWallet
    {
        private ExplorerClient _Client;
        private IMemoryCache _MemoryCache;
        public BTCPayWallet(ExplorerClient client, IMemoryCache memoryCache, BTCPayNetwork network)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (memoryCache == null)
                throw new ArgumentNullException(nameof(memoryCache));
            _Client = client;
            _Network = network;
            _MemoryCache = memoryCache;
        }


        private readonly BTCPayNetwork _Network;
        public BTCPayNetwork Network
        {
            get
            {
                return _Network;
            }
        }

        public TimeSpan CacheSpan { get; private set; } = TimeSpan.FromMinutes(5);

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
            var tx = await _Client.GetTransactionAsync(txId, cancellation);
            return tx;
        }

        public void InvalidateCache(DerivationStrategyBase strategy)
        {
            _MemoryCache.Remove("CACHEDCOINS_" + strategy.ToString());
            _FetchingUTXOs.TryRemove(strategy.ToString(), out var unused);
        }
        ConcurrentDictionary<string, TaskCompletionSource<UTXOChanges>> _FetchingUTXOs = new ConcurrentDictionary<string, TaskCompletionSource<UTXOChanges>>();

        private async Task<UTXOChanges> GetUTXOChanges(DerivationStrategyBase strategy, CancellationToken cancellation)
        {
            var thisCompletionSource = new TaskCompletionSource<UTXOChanges>();
            var completionSource = _FetchingUTXOs.GetOrAdd(strategy.ToString(), (s) => thisCompletionSource);
            if (thisCompletionSource != completionSource)
                return await completionSource.Task;
            try
            {
                var utxos = await _MemoryCache.GetOrCreateAsync("CACHEDCOINS_" + strategy.ToString(), async entry =>
                {
                    var now = DateTimeOffset.UtcNow;
                    UTXOChanges result = null;
                    try
                    {
                        result = await _Client.GetUTXOsAsync(strategy, null, false, cancellation).ConfigureAwait(false);
                    }
                    catch
                    {
                        Logs.PayServer.LogError($"{Network.CryptoCode}: Call to NBXplorer GetUTXOsAsync timed out, this should never happen, please report this issue to NBXplorer developers");
                        throw;
                    }
                    var spentTime = DateTimeOffset.UtcNow - now;
                    if (spentTime.TotalSeconds > 30)
                    {
                        Logs.PayServer.LogWarning($"{Network.CryptoCode}: NBXplorer took {(int)spentTime.TotalSeconds} seconds to reply, there is something wrong, please report this issue to NBXplorer developers");
                    }
                    entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                    return result;
                });
                _FetchingUTXOs.TryRemove(strategy.ToString(), out var unused);
                completionSource.TrySetResult(utxos);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
            finally
            {
                _FetchingUTXOs.TryRemove(strategy.ToString(), out var unused);
            }
            return await completionSource.Task;
        }

        public Task<GetTransactionsResponse> FetchTransactions(DerivationStrategyBase derivationStrategyBase)
        {
            return _Client.GetTransactionsAsync(derivationStrategyBase, null, false);
        }

        public Task<BroadcastResult[]> BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }



        public async Task<ReceivedCoin[]> GetUnspentCoins(DerivationStrategyBase derivationStrategy, CancellationToken cancellation = default(CancellationToken))
        {
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            return (await GetUTXOChanges(derivationStrategy, cancellation))
                          .GetUnspentUTXOs()
                          .Select(c => new ReceivedCoin()
                          {
                              Coin = c.AsCoin(derivationStrategy),
                              KeyPath = c.KeyPath,
                              Timestamp = c.Timestamp
                          }).ToArray();
        }

        public async Task<Money> GetBalance(DerivationStrategyBase derivationStrategy, CancellationToken cancellation = default(CancellationToken))
        {
            UTXOChanges changes = await GetUTXOChanges(derivationStrategy, cancellation);
            return changes.GetUnspentUTXOs().Select(c => c.Value).Sum();
        }
    }
}
