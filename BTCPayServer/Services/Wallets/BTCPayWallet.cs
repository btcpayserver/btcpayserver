using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

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
        public long Confirmations { get; set; }
        public BitcoinAddress Address { get; set; }
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
        public WalletRepository WalletRepository { get; }
        public NBXplorerConnectionFactory NbxplorerConnectionFactory { get; }
        public Logs Logs { get; }

        private readonly ExplorerClient _Client;
        private readonly IMemoryCache _MemoryCache;
        public BTCPayWallet(ExplorerClient client, IMemoryCache memoryCache, BTCPayNetwork network,
            WalletRepository walletRepository,
            ApplicationDbContextFactory dbContextFactory, NBXplorerConnectionFactory nbxplorerConnectionFactory, Logs logs)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(memoryCache);
            Logs = logs;
            _Client = client;
            _Network = network;
            WalletRepository = walletRepository;
            _dbContextFactory = dbContextFactory;
            NbxplorerConnectionFactory = nbxplorerConnectionFactory;
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

        public async Task<KeyPathInformation> ReserveAddressAsync(string storeId, DerivationStrategyBase derivationStrategy, string generatedBy)
        {
            if (storeId != null)
                ArgumentNullException.ThrowIfNull(generatedBy);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            // Might happen on some broken install
            if (pathInfo == null)
            {
                await _Client.TrackAsync(derivationStrategy).ConfigureAwait(false);
                pathInfo = await _Client.GetUnusedAsync(derivationStrategy, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            }
            if (storeId != null)
            {
                await WalletRepository.EnsureWalletObject(
                    new WalletObjectId(new WalletId(storeId, Network.CryptoCode), WalletObjectData.Types.Address, pathInfo.Address.ToString()),
                    new JObject() { ["generatedBy"] = generatedBy });
            }
            return pathInfo;
        }

        public async Task<(BitcoinAddress, KeyPath)> GetChangeAddressAsync(DerivationStrategyBase derivationStrategy)
        {
            ArgumentNullException.ThrowIfNull(derivationStrategy);
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
            ArgumentNullException.ThrowIfNull(txId);
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
        List<TransactionInformation> dummy = new List<TransactionInformation>();
        public async Task<IList<TransactionHistoryLine>> FetchTransactionHistory(DerivationStrategyBase derivationStrategyBase, int? skip = null, int? count = null, TimeSpan? interval = null, CancellationToken cancellationToken = default)
        {
            // This is two paths:
            // * Sometimes we can ask the DB to do the filtering of rows: If that's the case, we should try to filter at the DB level directly as it is the most efficient.
            // * Sometimes we can't query the DB or the given network need to do additional filtering. In such case, we can't really filter at the DB level, and we need to fetch all transactions in memory.
            var needAdditionalFiltering = _Network.FilterValidTransactions(dummy) != dummy;
            if (!NbxplorerConnectionFactory.Available || needAdditionalFiltering)
            {
                var txs = await FetchTransactions(derivationStrategyBase);
                var txinfos = txs.UnconfirmedTransactions.Transactions.Concat(txs.ConfirmedTransactions.Transactions)
                    .OrderByDescending(t => t.Timestamp)
                    .Skip(skip is null ? 0 : skip.Value)
                    .Take(count is null ? int.MaxValue : count.Value);
                var lines = new List<TransactionHistoryLine>(Math.Min((count is int v ? v : int.MaxValue), txs.UnconfirmedTransactions.Transactions.Count + txs.ConfirmedTransactions.Transactions.Count));
                DateTimeOffset? timestampLimit = interval is TimeSpan i ? DateTimeOffset.UtcNow - i : null;
                foreach (var t in txinfos)
                {
                    if (timestampLimit is DateTimeOffset l &&
                        t.Timestamp <= l)
                        break;
                    lines.Add(FromTransactionInformation(t));
                }
                return lines;
            }
            // This call is more efficient for big wallets, as it doesn't need to load all transactions from the history
            else
            {
                await using var ctx = await NbxplorerConnectionFactory.OpenConnection();
                var cmd = new CommandDefinition(
                    commandText: "SELECT r.tx_id, r.seen_at, t.blk_id, t.blk_height, r.balance_change, r.asset_id, COALESCE((SELECT height FROM get_tip('BTC')) - t.blk_height + 1, 0) AS confs " +
                    "FROM get_wallets_recent(@wallet_id, @code, @interval, @count, @skip) r " +
                    "JOIN txs t USING (code, tx_id) " +
                    "ORDER BY r.seen_at DESC",
                    parameters: new
                    {
                        wallet_id = NBXplorer.Client.DBUtils.nbxv1_get_wallet_id(Network.CryptoCode, derivationStrategyBase.ToString()),
                        code = Network.CryptoCode,
                        count = count == int.MaxValue ? null : count,
                        skip = skip,
                        interval = interval is TimeSpan t ? t : TimeSpan.FromDays(365 * 1000)
                    },
                    cancellationToken: cancellationToken);
                var rows = await ctx.QueryAsync<(string tx_id, DateTimeOffset seen_at, string blk_id, long? blk_height, long balance_change, string asset_id, long confs)>(cmd);
                rows.TryGetNonEnumeratedCount(out int c);
                var lines = new List<TransactionHistoryLine>(c);
                foreach (var row in rows)
                {
                    lines.Add(new TransactionHistoryLine()
                    {
                        BalanceChange = string.IsNullOrEmpty(row.asset_id) ? Money.Satoshis(row.balance_change) : new AssetMoney(uint256.Parse(row.asset_id), row.balance_change),
                        Height = row.blk_height,
                        SeenAt = row.seen_at,
                        TransactionId = uint256.Parse(row.tx_id),
                        Confirmations = row.confs,
                        BlockHash = string.IsNullOrEmpty(row.asset_id) ? null : uint256.Parse(row.blk_id)
                    });
                }
                return lines;
            }
        }

        private static TransactionHistoryLine FromTransactionInformation(TransactionInformation t)
        {
            return new TransactionHistoryLine()
            {
                BalanceChange = t.BalanceChange,
                Confirmations = t.Confirmations,
                Height = t.Height,
                SeenAt = t.Timestamp,
                TransactionId = t.TransactionId
            };
        }

        private async Task<GetTransactionsResponse> FetchTransactions(DerivationStrategyBase derivationStrategyBase)
        {
            var transactions = await _Client.GetTransactionsAsync(derivationStrategyBase);
            return FilterValidTransactions(transactions);
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

        public async Task<TransactionHistoryLine> FetchTransaction(DerivationStrategyBase derivationStrategyBase, uint256 transactionId)
        {
            var tx = await _Client.GetTransactionAsync(derivationStrategyBase, transactionId);
            if (tx is null || !_Network.FilterValidTransactions(new List<TransactionInformation>() { tx }).Any())
            {
                return null;
            }

            return FromTransactionInformation(tx);
        }

        public Task<BroadcastResult[]> BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }

        public async Task<ReceivedCoin[]> GetUnspentCoins(
            DerivationStrategyBase derivationStrategy,
            bool excludeUnconfirmed = false,
            CancellationToken cancellation = default(CancellationToken)
        )
        {
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            return (await GetUTXOChanges(derivationStrategy, cancellation))
                          .GetUnspentUTXOs(excludeUnconfirmed)
                          .Select(c => new ReceivedCoin()
                          {
                              KeyPath = c.KeyPath,
                              Value = c.Value,
                              Timestamp = c.Timestamp,
                              OutPoint = c.Outpoint,
                              ScriptPubKey = c.ScriptPubKey,
                              Coin = c.AsCoin(derivationStrategy),
                              Confirmations = c.Confirmations,
                              // Some old version of NBX doesn't have Address in this call
                              Address = c.Address ?? c.ScriptPubKey.GetDestinationAddress(Network.NBitcoinNetwork)
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

    public class TransactionHistoryLine
    {
        public DateTimeOffset SeenAt { get; set; }
        public long? Height { get; set; }
        public long Confirmations { get; set; }
        public uint256 TransactionId { get; set; }
        public uint256 BlockHash { get; set; }
        public IMoney BalanceChange { get; set; }
    }
}
