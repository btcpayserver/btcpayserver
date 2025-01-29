using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Dapper;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Services.TransactionLinkProviders;
using static NBitcoin.Protocol.Behaviors.ChainBehavior;

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


#nullable enable
    public record BumpableInfo(bool RBF, bool CPFP, ReplacementInfo? ReplacementInfo);
#nullable restore
    public enum BumpableSupport
    {
        NotConfigured,
        NotCompatible,
        NotSynched,
        Ok
    }
    public class BumpableTransactions : Dictionary<uint256, BumpableInfo>
    {
        public BumpableTransactions()
        {
        }

        public BumpableSupport Support { get; internal set; }
    }
    public class BTCPayWallet
    {
        public WalletRepository WalletRepository { get; }
        public NBXplorerDashboard Dashboard { get; }
        public NBXplorerConnectionFactory NbxplorerConnectionFactory { get; }
        public Logs Logs { get; }

        private readonly ExplorerClient _Client;
        private readonly IMemoryCache _MemoryCache;
        public BTCPayWallet(ExplorerClient client, IMemoryCache memoryCache, BTCPayNetwork network,
            WalletRepository walletRepository,
            NBXplorerDashboard dashboard,
            ApplicationDbContextFactory dbContextFactory, NBXplorerConnectionFactory nbxplorerConnectionFactory, Logs logs)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(memoryCache);
            Logs = logs;
            _Client = client;
            _Network = network;
            WalletRepository = walletRepository;
            Dashboard = dashboard;
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
                        BlockHash = string.IsNullOrEmpty(row.blk_id) ? null : uint256.Parse(row.blk_id)
                    });
                }
                return lines;
            }
        }
        public async Task<BumpableTransactions> GetBumpableTransactions(DerivationStrategyBase derivationStrategyBase, CancellationToken cancellationToken)
        {
            var result = new BumpableTransactions();
            result.Support = BumpableSupport.NotConfigured;
            if (!NbxplorerConnectionFactory.Available)
                return result;
            result.Support = BumpableSupport.NotCompatible;
            var state = this.Dashboard.Get(Network.CryptoCode);
            if (AsVersion(state?.Status?.Version ?? "") < new Version("2.5.22"))
                return result;
            result.Support = BumpableSupport.NotSynched;
            if (state?.Status.IsFullySynched is not true)
                return result;
            result.Support = BumpableSupport.Ok;
            await using var ctx = await NbxplorerConnectionFactory.OpenConnection();
            var cmd = new CommandDefinition(
                    commandText: """
                    WITH unconfs AS (
                    	SELECT code, tx_id, raw
                    	FROM txs
                    	WHERE code=@code AND raw IS NOT NULL AND mempool IS TRUE AND replaced_by IS NULL AND blk_id IS NULL),
                    tracked_txs AS (
                    SELECT code, tx_id, 
                            COUNT(*) FILTER (WHERE is_out IS FALSE) input_count,
                            COUNT(*) FILTER (WHERE is_out IS TRUE AND feature = 'Change') change_count
                        FROM nbxv1_tracked_txs
                    	WHERE code = @code AND wallet_id=@walletId AND mempool IS TRUE AND replaced_by IS NULL AND blk_id IS NULL
                    	GROUP BY code, tx_id
                    ),
                    unspent_utxos AS (
                        SELECT code, tx_id, COUNT(*) FILTER (WHERE input_tx_id IS NULL) unspent_count
                        FROM wallets_utxos
                        WHERE code = @code AND wallet_id=@walletId AND mempool IS TRUE AND replaced_by IS NULL AND blk_id IS NULL
                        GROUP BY code, tx_id
                    )
                    SELECT tt.tx_id, u.raw, tt.input_count, tt.change_count, uu.unspent_count FROM unconfs u
                    JOIN tracked_txs tt USING (code, tx_id)
                    JOIN unspent_utxos uu USING (code, tx_id);
                    """,
                    parameters: new
                    {
                        code = Network.CryptoCode,
                        walletId = NBXplorer.Client.DBUtils.nbxv1_get_wallet_id(Network.CryptoCode, derivationStrategyBase.ToString())
                    },
                    cancellationToken: cancellationToken);

            // We can only replace mempool transaction where all inputs belong to us. (output_count and input_count count those belonging to us)
            var rows = (await ctx.QueryAsync<(string tx_id, byte[] raw, int input_count, int change_count, int unspent_count)>(cmd));
            if (Enumerable.TryGetNonEnumeratedCount(rows, out int c) && c == 0)
                return result;

            HashSet<uint256> canRBF = new();
            HashSet<uint256> canCPFP = new();
            foreach (var r in rows)
            {
                Transaction tx;
                try
                {
                    tx = Transaction.Load(r.raw, Network.NBitcoinNetwork);
                }
                catch
                {
                    continue;
                }
                if ((state.MempoolInfo?.FullRBF is true || tx.RBF) && tx.Inputs.Count == r.input_count &&
                    r.change_count > 0)
                {
                    canRBF.Add(uint256.Parse(r.tx_id));
                }
                if (r.unspent_count > 0)
                {
                    canCPFP.Add(uint256.Parse(r.tx_id));
                }
            }
            
            // Then only transactions that doesn't have any descendant (outside itself)
            var entries = await _Client.RPCClient.FetchMempoolEntries(canRBF.Concat(canCPFP).ToHashSet(), cancellationToken);
            foreach (var entry in entries)
            {
				if (entry.Value.DescendantCount != 1)
                {
                    canRBF.Remove(entry.Key);
                }
            }
            if (state is not
                {
                    MempoolInfo:
                    {
                        IncrementalRelayFeeRate: { } incRelayFeeRate,
                        MempoolMinfeeRate: { } minFeeRate
                    }
                })
            {
                incRelayFeeRate = new FeeRate(1.0m);
                minFeeRate = new FeeRate(1.0m);
            }
            foreach (var r in rows)
            {
                var id = uint256.Parse(r.tx_id);
				if (!entries.TryGetValue(id, out var mempoolEntry))
                {
					canCPFP.Remove(id);
					canRBF.Remove(id);
				}
                result.Add(id, new(canRBF.Contains(id), canCPFP.Contains(id), new ReplacementInfo(mempoolEntry, incRelayFeeRate, minFeeRate)));
            }
            return result;
        }

        private Version AsVersion(string version)
        {
            if (Version.TryParse(version.Split('-').FirstOrDefault(), out var v))
                return v;
            return new Version("0.0.0.0");
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
    public record ReplacementInfo(MempoolEntry Entry, FeeRate IncrementalRelayFee, FeeRate MinMempoolFeeRate)
    {
        public record BumpResult(Money NewTxFee, Money BumpTxFee, FeeRate NewTxFeeRate, FeeRate NewEffectiveFeeRate);
        public BumpResult CalculateBumpResult(FeeRate newEffectiveFeeRate)
        {
            var packageFeeRate = GetEffectiveFeeRate();
            var newTotalFee = GetFeeRoundUp(newEffectiveFeeRate, GetPackageVirtualSize());
            var oldTotalFee = GetPackageFee();
            var bump = newTotalFee - oldTotalFee;
            var newTxFee = Entry.BaseFee + bump;
            var newTxFeeRate = new FeeRate(newTxFee, Entry.VirtualSizeBytes);
            var totalFeeRate = new FeeRate(newTotalFee, GetPackageVirtualSize());
            return new BumpResult(newTxFee, bump, newTxFeeRate, totalFeeRate);
        }
        static Money GetFeeRoundUp(FeeRate feeRate, int vsize) => (Money)((feeRate.FeePerK.Satoshi * vsize + 999) / 1000);
        public FeeRate CalculateNewMinFeeRate()
        {
            var packageFeeRate = GetEffectiveFeeRate();
            var newMinFeeRate = new FeeRate(packageFeeRate.SatoshiPerByte + IncrementalRelayFee.SatoshiPerByte);
            var bump = CalculateBumpResult(newMinFeeRate);

            if (bump.NewTxFeeRate < MinMempoolFeeRate)
            {
                // We need to pay a bit more fee for the transaction to be relayed
                var newTxFee = GetFeeRoundUp(MinMempoolFeeRate, Entry.VirtualSizeBytes);
                newMinFeeRate = new FeeRate(GetPackageFee() - Entry.BaseFee + newTxFee, GetPackageVirtualSize());
            }
            return newMinFeeRate;
        }

        public int GetPackageVirtualSize() =>
            Entry.DescendantVirtualSizeBytes + Entry.AncestorVirtualSizeBytes - Entry.VirtualSizeBytes;
        public Money GetPackageFee() =>
            Entry.DescendantFees + Entry.AncestorFees - Entry.BaseFee;
        // Note: This isn't a correct way to calculate the package fee rate, but it is good enough for our purpose.
        // It is only accounting the fee from direct ancestors/descendants. (not of uncles/cousins/brothers)
        // Another more precise fee rate is documented https://x.com/ajtowns/status/1886025911562309967
        // But it is more complex to calculate, as we need to recursively fetch the mempool for all the descendants
        public FeeRate GetEffectiveFeeRate() => new FeeRate(GetPackageFee(), GetPackageVirtualSize());
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
