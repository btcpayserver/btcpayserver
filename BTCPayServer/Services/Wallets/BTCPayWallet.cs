using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Services.Wallets
{
    public class ReceivedCoin
    {
        public Script ScriptPubKey { get; set; }
        public OutPoint OutPoint { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public KeyPath KeyPath { get; set; }
        public IMoney Value { get; set; }
        public Coin Coin { get; set; }
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
        private readonly ExplorerClient _Client;
        private readonly IMemoryCache _MemoryCache;
        public BTCPayWallet(ExplorerClient client, IMemoryCache memoryCache, BTCPayNetwork network,
            ApplicationDbContextFactory dbContextFactory)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (memoryCache == null)
                throw new ArgumentNullException(nameof(memoryCache));
            _Client = client;
            _Network = network;
            _dbContextFactory = dbContextFactory;
            _MemoryCache = memoryCache;
        }


        private readonly BTCPayNetwork _Network;
        private readonly ApplicationDbContextFactory _dbContextFactory;

        public BTCPayNetwork Network
        {
            get
            {
                return _Network;
            }
        }

        public TimeSpan CacheSpan { get; private set; } = TimeSpan.FromMinutes(5);

        public async Task<KeyPathInformation> ReserveAddressAsync(DerivationStrategyBase derivationStrategy)
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
            return pathInfo;
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
            await _Client.TrackAsync(derivationStrategy, new TrackWalletRequest()
            {
                Wait = false
            });
        }

        public async Task<TransactionResult> GetTransactionAsync(uint256 txId, bool includeOffchain = false, CancellationToken cancellation = default(CancellationToken))
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            var tx = await _Client.GetTransactionAsync(txId, cancellation);
            if (tx is null && includeOffchain)
            {
                var offchainTx = await GetOffchainTransactionAsync(txId);
                if (offchainTx != null)
                    tx = new TransactionResult()
                    {
                        Confirmations = -1,
                        TransactionHash = offchainTx.GetHash(),
                        Transaction = offchainTx
                    };
            }
            return tx;
        }

        public async Task<Transaction> GetOffchainTransactionAsync(uint256 txid)
        {
            using var ctx = this._dbContextFactory.CreateContext();
            var txData = await ctx.OffchainTransactions.FindAsync(txid.ToString());
            if (txData is null)
                return null;
            return Transaction.Load(txData.Blob, this._Network.NBitcoinNetwork);
        }
        public async Task SaveOffchainTransactionAsync(Transaction tx)
        {
            using var ctx = this._dbContextFactory.CreateContext();
            ctx.OffchainTransactions.Add(new OffchainTransactionData()
            {
                Id = tx.GetHash().ToString(),
                Blob = tx.ToBytes()
            });
            try
            {
                await ctx.SaveChangesAsync();
            }
            // Already in db
            catch (DbUpdateException)
            {
            }
        }

        public void InvalidateCache(DerivationStrategyBase strategy)
        {
            _MemoryCache.Remove("CACHEDCOINS_" + strategy.ToString());
            _MemoryCache.Remove("CACHEDBALANCE_" + strategy.ToString());
            _FetchingUTXOs.TryRemove(strategy.ToString(), out var unused);
        }

        readonly ConcurrentDictionary<string, TaskCompletionSource<UTXOChanges>> _FetchingUTXOs = new ConcurrentDictionary<string, TaskCompletionSource<UTXOChanges>>();

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
                        result = await _Client.GetUTXOsAsync(strategy, cancellation).ConfigureAwait(false);
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

        public async Task<GetTransactionsResponse> FetchTransactions(DerivationStrategyBase derivationStrategyBase)
        {
            return FilterValidTransactions(await _Client.GetTransactionsAsync(derivationStrategyBase));
        }

        private GetTransactionsResponse FilterValidTransactions(GetTransactionsResponse response)
        {
            return new GetTransactionsResponse()
            {
                Height = response.Height,
                UnconfirmedTransactions =
                    new TransactionInformationSet()
                    {
                        Transactions = _Network.FilterValidTransactions(response.UnconfirmedTransactions.Transactions)
                    },
                ConfirmedTransactions =
                    new TransactionInformationSet()
                    {
                        Transactions = _Network.FilterValidTransactions(response.ConfirmedTransactions.Transactions)
                    },
                ReplacedTransactions = new TransactionInformationSet()
                {
                    Transactions = _Network.FilterValidTransactions(response.ReplacedTransactions.Transactions)
                }
            };
        }

        public async Task<TransactionInformation> FetchTransaction(DerivationStrategyBase derivationStrategyBase, uint256 transactionId)
        {
            var tx = await _Client.GetTransactionAsync(derivationStrategyBase, transactionId);
            if (tx is null || !_Network.FilterValidTransactions(new List<TransactionInformation>() {tx}).Any())
            {
                return null;
            }

            return tx;
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
                              KeyPath = c.KeyPath,
                              Value = c.Value,
                              Timestamp = c.Timestamp,
                              OutPoint = c.Outpoint,
                              ScriptPubKey = c.ScriptPubKey,
                              Coin = c.AsCoin(derivationStrategy)
                          }).ToArray();
        }

        public Task<GetBalanceResponse> GetBalance(DerivationStrategyBase derivationStrategy, CancellationToken cancellation = default(CancellationToken))
        {
            return _MemoryCache.GetOrCreateAsync("CACHEDBALANCE_" + derivationStrategy.ToString(), async (entry) =>
            {
                var result = await _Client.GetBalanceAsync(derivationStrategy, cancellation);
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + CacheSpan;
                return result;
            });
        }
    }
}
