using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWalletProvider
    {
        public WalletRepository WalletRepository { get; }
        public Logs Logs { get; }

        private readonly ExplorerClientProvider _Client;
        readonly BTCPayNetworkProvider _NetworkProvider;
        readonly IOptions<MemoryCacheOptions> _Options;
        public BTCPayWalletProvider(ExplorerClientProvider client,
                                    IOptions<MemoryCacheOptions> memoryCacheOption,
                                    Data.ApplicationDbContextFactory dbContextFactory,
                                    BTCPayNetworkProvider networkProvider,
                                    NBXplorerConnectionFactory nbxplorerConnectionFactory,
                                    WalletRepository walletRepository,
                                    Logs logs)
        {
            ArgumentNullException.ThrowIfNull(client);
            this.Logs = logs;
            _Client = client;
            _NetworkProvider = networkProvider;
            WalletRepository = walletRepository;
            _Options = memoryCacheOption;

            foreach (var network in networkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                var explorerClient = _Client.GetExplorerClient(network.CryptoCode);
                if (explorerClient == null)
                    continue;
                _Wallets.Add(network.CryptoCode.ToUpperInvariant(), new BTCPayWallet(explorerClient, new MemoryCache(_Options), network, WalletRepository, dbContextFactory, nbxplorerConnectionFactory, Logs));
            }
        }

        readonly Dictionary<string, BTCPayWallet> _Wallets = new Dictionary<string, BTCPayWallet>();

        public BTCPayWallet GetWallet(BTCPayNetworkBase network)
        {
            ArgumentNullException.ThrowIfNull(network);
            return GetWallet(network.CryptoCode);
        }
        public BTCPayWallet GetWallet(string cryptoCode)
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            _Wallets.TryGetValue(cryptoCode.ToUpperInvariant(), out var result);
            return result;
        }

        public bool IsAvailable(BTCPayNetworkBase network)
        {
            return _Client.IsAvailable(network);
        }

        public IEnumerable<BTCPayWallet> GetWallets()
        {
            foreach (var w in _Wallets)
                yield return w.Value;
        }
    }
    
    
    public class PendingTransactionService:EventHostedServiceBase
    {
        private readonly DelayedTransactionBroadcaster _broadcaster;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly ExplorerClientProvider _explorerClientProvider;

        public PendingTransactionService(
            DelayedTransactionBroadcaster broadcaster,
            BTCPayNetworkProvider networkProvider,
            ApplicationDbContextFactory dbContextFactory,
            EventAggregator eventAggregator, 
            ILogger<PendingTransactionService> logger,
            ExplorerClientProvider explorerClientProvider ) : base(eventAggregator, logger)
        {
            _broadcaster = broadcaster;
            _networkProvider = networkProvider;
            _dbContextFactory = dbContextFactory;
            _explorerClientProvider = explorerClientProvider;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<NewOnChainTransactionEvent>();   
            base.SubscribeToEvents();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await  base.StartAsync(cancellationToken);
            _ = CheckForExpiry(CancellationToken);
        }
        
        private async Task CheckForExpiry(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var ctx = _dbContextFactory.CreateContext();
                var pendingTransactions = await ctx.PendingTransactions
                    .Where(p => p.Expiry <= DateTimeOffset.UtcNow && p.State == PendingTransactionState.Pending)
                    .ToArrayAsync(cancellationToken: cancellationToken);
                foreach (var pendingTransaction in pendingTransactions)
                {
                    pendingTransaction.State = PendingTransactionState.Expired;
                }

                await ctx.SaveChangesAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is NewOnChainTransactionEvent newTransactionEvent)
            {
                
                await using var ctx = _dbContextFactory.CreateContext();
                var txInputs = newTransactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs.Select(i => i.PrevOut.ToString()).ToArray();
                var txHash = newTransactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();
                var pendingTransactions = await ctx.PendingTransactions.Where(p => p.TransactionId == txHash || p.OutpointsUsed.Any(o => txInputs.Contains(o))).ToArrayAsync(cancellationToken: cancellationToken);
                if (!pendingTransactions.Any())
                {
                    return;
                }
                foreach (var pendingTransaction in pendingTransactions)
                {
                    if(pendingTransaction.TransactionId == txHash)
                    {
                        pendingTransaction.State = PendingTransactionState.Broadcast;
                        continue;
                    }

                    if(pendingTransaction.OutpointsUsed.Any(o => txInputs.Contains(o)))
                    {
                        pendingTransaction.State = PendingTransactionState.Invalidated;
                    }
                }
                await ctx.SaveChangesAsync(cancellationToken);
            }
            await  base.ProcessEvent(evt, cancellationToken);
        }
        
        public async Task<PendingTransaction> CreatePendingTransaction(string storeId, string cryptoCode, PSBT psbt, DateTimeOffset? expiry = null, CancellationToken cancellationToken = default)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                throw new NotSupportedException("CryptoCode not supported");
            }
            var txId = psbt.GetGlobalTransaction().GetHash();
            await using var ctx = _dbContextFactory.CreateContext();
            var pendingTransaction = new PendingTransaction
            {
                CryptoCode = cryptoCode,
                TransactionId = txId.ToString(),
                State = PendingTransactionState.Pending,
                OutpointsUsed = psbt.Inputs.Select(i => i.PrevOut.ToString()).ToArray(),
                Expiry = expiry,
                StoreId = storeId,
            };
            pendingTransaction.SetBlob(new PendingTransactionBlob
            {
                PSBT = psbt.ToBase64()
            });
            ctx.PendingTransactions.Add(pendingTransaction);
            await ctx.SaveChangesAsync(cancellationToken);
            return pendingTransaction;
        }
        
        
        public async Task<PendingTransaction?> CollectSignature(string cryptoCode, PSBT psbt, bool broadcastIfComplete, CancellationToken cancellationToken)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return null;
            }

            var txId = psbt.GetGlobalTransaction().GetHash();
            
            await using var ctx = _dbContextFactory.CreateContext();
            var pendingTransaction = await ctx.PendingTransactions.FindAsync( new object[]{ cryptoCode, txId.ToString()}, cancellationToken);
            if (pendingTransaction is null)
            {
                return null;
            }
            if(pendingTransaction.State != PendingTransactionState.Pending)
            {
                return null;
            }
            var blob = pendingTransaction.GetBlob();
            var originalPsbtWorkingCopy = PSBT.Parse(blob.PSBT, network.NBitcoinNetwork);
            foreach (var collectedSignature in blob.CollectedSignatures)
            {
                var collectedPsbt = PSBT.Parse(collectedSignature.ReceivedPSBT, network.NBitcoinNetwork);
                originalPsbtWorkingCopy = originalPsbtWorkingCopy.Combine(collectedPsbt);
            }
            var originalPsbtWorkingCopyWithNewPsbt = originalPsbtWorkingCopy.Combine(psbt);
            //check if we have more signatures than before
            if (originalPsbtWorkingCopyWithNewPsbt.Inputs.All(i => i.PartialSigs.Count >= originalPsbtWorkingCopy.Inputs[(int) i.Index].PartialSigs.Count))
            {
                blob.CollectedSignatures.Add(new CollectedSignature
                {
                    ReceivedPSBT = psbt.ToBase64(),
                    Timestamp = DateTimeOffset.UtcNow
                });
                pendingTransaction.SetBlob(blob);
            }

            if (originalPsbtWorkingCopyWithNewPsbt.TryFinalize(out _))
            {
                pendingTransaction.State = PendingTransactionState.Signed;
               
            }
            await ctx.SaveChangesAsync(cancellationToken);
            
            if (broadcastIfComplete && pendingTransaction.State == PendingTransactionState.Signed)
            {   
                var explorerClient = _explorerClientProvider.GetExplorerClient(network);
                var tx = originalPsbtWorkingCopyWithNewPsbt.ExtractTransaction();
                var result = await explorerClient.BroadcastAsync(tx, cancellationToken);
                if(result.Success)
                {
                    pendingTransaction.State = PendingTransactionState.Broadcast;
                    await ctx.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    await _broadcaster.Schedule(DateTimeOffset.Now,tx, network);
                }
            }
         
            return pendingTransaction;
        }

        public async Task<PendingTransaction?> GetPendingTransaction(string cryptoCode, string storeId, string txId)
        {
            
            await using var ctx = _dbContextFactory.CreateContext();
            return await ctx.PendingTransactions.FirstOrDefaultAsync(p => p.CryptoCode == cryptoCode && p.StoreId == storeId && p.TransactionId == txId);
        }
        public async Task<PendingTransaction[]> GetPendingTransactions(string cryptoCode, string storeId)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            return await ctx.PendingTransactions.Where(p => p.CryptoCode == cryptoCode && p.StoreId == storeId && (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed)).ToArrayAsync();
        }

        public async Task CancelPendingTransaction(string cryptoCode, string storeId, string transactionId)
        {
            
            await using var ctx = _dbContextFactory.CreateContext();
            var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
                p.CryptoCode == cryptoCode && p.StoreId == storeId && p.TransactionId == transactionId &&
                (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
            
            if (pt is null)
            {
                return;
            }
            pt.State = PendingTransactionState.Cancelled;
            await ctx.SaveChangesAsync();
        }
    }

   
    
}
