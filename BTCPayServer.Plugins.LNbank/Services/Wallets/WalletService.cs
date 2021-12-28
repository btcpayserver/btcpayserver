using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = BTCPayServer.Plugins.LNbank.Data.Models.Transaction;

namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class WalletService
    {
        private readonly ILogger _logger;
        private readonly BTCPayService _btcpayService;
        private readonly LNbankPluginDbContextFactory _dbContextFactory;
        private readonly IHubContext<TransactionHub> _transactionHub;
        private readonly Network _network;

        public WalletService(
            ILogger<WalletService> logger,
            IHubContext<TransactionHub> transactionHub,
            BTCPayService btcpayService,
            BTCPayNetworkProvider btcPayNetworkProvider,
            LNbankPluginDbContextFactory dbContextFactory)
        {
            _logger = logger;
            _btcpayService = btcpayService;
            _dbContextFactory = dbContextFactory;
            _transactionHub = transactionHub;
            _network = btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(BTCPayService.CryptoCode).NBitcoinNetwork;
        }

        public async Task<IEnumerable<Wallet>> GetWallets(WalletsQuery query)
        {
            
            await using var dbContext = _dbContextFactory.CreateContext();
            return await FilterWallets(dbContext.Wallets.AsQueryable(), query).ToListAsync();
        }

        private IQueryable<Wallet> FilterWallets(IQueryable<Wallet> queryable, WalletsQuery query)
        {
            if (query.UserId != null)
            {
                queryable = queryable.Where(wallet => query.UserId.Contains(wallet.UserId));
            }
            if (query.AccessKey != null)
            {
                queryable = queryable.Include(wallet => wallet.AccessKeys).Where(wallet =>
                    wallet.AccessKeys.Any(key => query.AccessKey.Contains(key.Key)));
            }

            if (query.WalletId != null)
            {
                queryable = queryable.Where(wallet => query.WalletId.Contains(wallet.WalletId));
            }

            if (query.IncludeTransactions)
            {
                queryable = queryable.Include(w => w.Transactions).AsNoTracking();
            }

            if (query.IncludeAccessKeys)
            {
                queryable = queryable.Include(w => w.AccessKeys).AsNoTracking();
            }

            return queryable;
        }

        public async Task<Wallet> GetWallet(WalletQuery query)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            var walletsQuery = new WalletsQuery()
            {
                IncludeTransactions = query.IncludeTransactions,
                IncludeAccessKeys = query.IncludeAccessKeys,
                UserId = query.UserId is null? null : new []{ query.UserId},
                WalletId = query.WalletId is null? null : new []{ query.WalletId},
                AccessKey = query.AccessKey is null? null : new []{ query.AccessKey},
            };
            return await FilterWallets(dbContext.Wallets.AsQueryable(), walletsQuery).FirstOrDefaultAsync();
        }

        public async Task<Transaction> Receive(Wallet wallet, long amount, string description) =>
            await Receive(wallet, amount, description, null, LightningInvoiceCreateRequest.ExpiryDefault);
        
        public async Task<Transaction> Receive(Wallet wallet, long amount, uint256 description) =>
            await Receive(wallet, amount, null, description, LightningInvoiceCreateRequest.ExpiryDefault);

        private async Task<Transaction> Receive(Wallet wallet, long amount, string description, uint256 descriptionHash, TimeSpan expiry)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            if (amount <= 0) throw new ArgumentException(nameof(amount));

            var data = await _btcpayService.CreateLightningInvoice(wallet.UserId, new LightningInvoiceCreateRequest
            {
                Amount = amount,
                Description = description,
                DescriptionHash = descriptionHash,
                Expiry = expiry
            });

            var entry = await dbContext.Transactions.AddAsync(new Transaction
            {
                WalletId = wallet.WalletId,
                InvoiceId = data.Id,
                Amount = data.Amount,
                ExpiresAt = data.ExpiresAt,
                PaymentRequest = data.BOLT11,
                Description = description?? descriptionHash.ToString()
            });
            await dbContext.SaveChangesAsync();

            return entry.Entity;
        }

        public async Task Send(Wallet wallet, BOLT11PaymentRequest bolt11, string paymentRequest)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            var amount = bolt11.MinimumAmount;

            if (bolt11.ExpiryDate <= DateTimeOffset.UtcNow)
            {
                throw new Exception($"Payment request already expired at {bolt11.ExpiryDate}.");
            }

            if (wallet.Balance < amount)
            {
                var balanceSats = wallet.Balance.ToUnit(LightMoneyUnit.Satoshi);
                var amountSats = amount.ToUnit(LightMoneyUnit.Satoshi);
                throw new Exception($"Insufficient balance: {balanceSats} sats, tried to send {amountSats} sats.");
            }

            // try internal payment and fall back to paying via the node
            Transaction internalReceivingTransaction = await GetTransaction(new TransactionQuery
            {
                PaymentRequest = paymentRequest, HasInvoiceId = true
            });
            
            if (internalReceivingTransaction != null)
            {
                if (internalReceivingTransaction.IsExpired)
                {
                    throw new Exception($"Payment request already expired at {internalReceivingTransaction.ExpiresAt}.");
                }
                if (internalReceivingTransaction.IsPaid)
                {
                    throw new Exception($"Payment request has already been paid.");
                }
            }
            else
            {
                await _btcpayService.PayLightningInvoice(wallet.UserId,
                                    new LightningInvoicePayRequest {PaymentRequest = paymentRequest});
            }
            
            var executionStrategy = dbContext.Database.CreateExecutionStrategy();

            await executionStrategy.ExecuteAsync(async () =>
            {
                // https://docs.microsoft.com/en-us/ef/core/saving/transactions#controlling-transactions
                await using var dbTransaction = await dbContext.Database.BeginTransactionAsync();
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    await dbContext.Transactions.AddAsync(new Transaction
                    {
                        WalletId = wallet.WalletId,
                        PaymentRequest = paymentRequest,
                        Amount = amount,
                        AmountSettled = new LightMoney(amount.MilliSatoshi * -1),
                        ExpiresAt = bolt11.ExpiryDate,
                        Description = bolt11.ShortDescription,
                        PaidAt = now
                    });
                    await dbContext.SaveChangesAsync();

                    if (internalReceivingTransaction != null)
                    {
                        await MarkTransactionPaid(internalReceivingTransaction, amount, now);
                    }
                    await dbTransaction.CommitAsync();
                }
                catch (Exception)
                {
                    await dbTransaction.RollbackAsync();
                }
            });
        }

        public async Task AddOrUpdateWallet(Wallet wallet)
        {
            await using var dbContext = _dbContextFactory.CreateContext();

            if (string.IsNullOrEmpty(wallet.WalletId))
            {
                wallet.AccessKeys ??= new List<AccessKey>();
                wallet.AccessKeys.Add(new AccessKey()
                {
                    Key = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20))
                });
                await dbContext.Wallets.AddAsync(wallet);
            }
            else
            {
                var entry = dbContext.Entry(wallet);
                entry.State = EntityState.Modified;
            }
            await dbContext.SaveChangesAsync();
        }
        

        public async Task RemoveWallet(Wallet wallet)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            dbContext.Wallets.Remove(wallet);
            await dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<Transaction>> GetPendingTransactions()
        {
            return await GetTransactions(new TransactionsQuery
            {
                IncludingPending = true,
                IncludingExpired = false,
                IncludingPaid = false
            });
        }

        public async Task CheckPendingTransaction(Transaction transaction, CancellationToken cancellationToken = default)
        {
            var invoice = await _btcpayService.GetLightningInvoice(transaction.InvoiceId, cancellationToken);
            if (invoice.Status == LightningInvoiceStatus.Paid)
            {
                await MarkTransactionPaid(transaction, invoice.AmountReceived, invoice.PaidAt);
            }
        }

        public async Task<Transaction> GetTransaction(TransactionQuery query)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            IQueryable<Transaction> queryable = dbContext.Transactions.AsQueryable();

            if (query.WalletId != null)
            {
                var walletQuery = new WalletQuery
                {
                    WalletId = query.WalletId,
                    IncludeTransactions = true
                };

                if (query.UserId != null) walletQuery.UserId = query.UserId;

                var wallet = await GetWallet(walletQuery);

                if (wallet == null) return null;

                queryable = wallet.Transactions.AsQueryable();
            }

            if (query.InvoiceId != null)
            {
                queryable = queryable.Where(t => t.InvoiceId == query.InvoiceId);
            }
            else if (query.HasInvoiceId)
            {
                queryable = queryable.Where(t => t.InvoiceId != null);
            }

            if (query.TransactionId != null)
            {
                queryable = queryable.Where(t => t.TransactionId == query.TransactionId);
            }

            if (query.PaymentRequest != null)
            {
                queryable = queryable.Where(t => t.PaymentRequest == query.PaymentRequest);
            }

            return queryable.SingleOrDefault();
        }

        public async Task UpdateTransaction(Transaction transaction)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            var entry = dbContext.Entry(transaction);
            entry.State = EntityState.Modified;

            await dbContext.SaveChangesAsync();
        }

        public BOLT11PaymentRequest ParsePaymentRequest(string payReq)
        {
            return BOLT11PaymentRequest.Parse(payReq, _network);
        }

        private async Task<IEnumerable<Transaction>> GetTransactions(TransactionsQuery query)
        {
            await using var dbContext = _dbContextFactory.CreateContext();
            var queryable = dbContext.Transactions.AsQueryable();

            if (query.UserId != null) query.IncludeWallet = true;

            if (query.WalletId != null)
            {
                queryable = queryable.Where(t => t.WalletId == query.WalletId);
            }
            if (query.IncludeWallet)
            {
                queryable = queryable.Include(t => t.Wallet).AsNoTracking();
            }
            if (query.UserId != null)
            {
                queryable = queryable.Where(t => t.Wallet.UserId == query.UserId);
            }

            if (!query.IncludingPaid)
            {
                queryable = queryable.Where(t => t.PaidAt == null);
            }

            if (!query.IncludingPending)
            {
                queryable = queryable.Where(t => t.PaidAt != null);
            }

            if (!query.IncludingExpired)
            {
                var enumerable = queryable.AsEnumerable(); // Switch to client side filtering
                return enumerable.Where(t => t.ExpiresAt > DateTimeOffset.UtcNow).ToList();
            }

            return await queryable.ToListAsync();
        }

        private async Task MarkTransactionPaid(Transaction transaction, LightMoney amountSettled, DateTimeOffset? date)
        {
            _logger.LogInformation($"Marking transaction {transaction.TransactionId} as paid");

            transaction.AmountSettled = amountSettled;
            transaction.PaidAt = date;

            await UpdateTransaction(transaction);
            await _transactionHub.Clients.All.SendAsync("transaction-update", new
            {
                transaction.TransactionId,
                transaction.InvoiceId,
                transaction.WalletId,
                transaction.Status,
                transaction.IsPaid,
                transaction.IsExpired,
                Event = "paid"
            });
        }

        public async Task Cancel(string invoiceId)
        {
            var transaction = await GetTransaction(new TransactionQuery() { InvoiceId = invoiceId });
            if (transaction.SetCancelled())
            {
                await UpdateTransaction(transaction);
                await _transactionHub.Clients.All.SendAsync("transaction-update", new
                {
                    transaction.TransactionId,
                    transaction.InvoiceId,
                    transaction.WalletId,
                    transaction.Status,
                    transaction.IsPaid,
                    transaction.IsExpired,
                    Event = "cancelled"
                });
            }
        }
    }
}
