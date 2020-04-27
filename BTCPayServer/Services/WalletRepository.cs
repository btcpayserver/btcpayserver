using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services
{
    public class WalletRepository
    {
        private ApplicationDbContextFactory _ContextFactory;

        public WalletRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task SetWalletInfo(WalletId walletId, WalletBlobInfo blob)
        {
            if (walletId == null)
                throw new ArgumentNullException(nameof(walletId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                var walletData = new WalletData() { Id = walletId.ToString() };
                walletData.SetBlobInfo(blob);
                var entity = await ctx.Wallets.AddAsync(walletData);
                entity.State = EntityState.Modified;
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException) // Does not exists
                {
                    entity.State = EntityState.Added;
                    await ctx.SaveChangesAsync();
                }
            }
        }

        public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId)
        {
            if (walletId == null)
                throw new ArgumentNullException(nameof(walletId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                return (await ctx.WalletTransactions
                                .Where(w => w.WalletDataId == walletId.ToString())
                                .Select(w => w)
                                .ToArrayAsync())
                                .ToDictionary(w => w.TransactionId, w => w.GetBlobInfo());
            }
        }

        public async Task<WalletBlobInfo> GetWalletInfo(WalletId walletId)
        {
            if (walletId == null)
                throw new ArgumentNullException(nameof(walletId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                var data = await ctx.Wallets
                     .Where(w => w.Id == walletId.ToString())
                     .Select(w => w)
                     .FirstOrDefaultAsync();
                return data?.GetBlobInfo() ?? new WalletBlobInfo();
            }
        }

        public async Task SetWalletTransactionInfo(WalletId walletId, string transactionId, WalletTransactionInfo walletTransactionInfo)
        {
            if (walletId == null)
                throw new ArgumentNullException(nameof(walletId));
            if (transactionId == null)
                throw new ArgumentNullException(nameof(transactionId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                var walletData = new WalletTransactionData() { WalletDataId = walletId.ToString(), TransactionId = transactionId };
                walletData.SetBlobInfo(walletTransactionInfo);
                var entity = await ctx.WalletTransactions.AddAsync(walletData);
                entity.State = EntityState.Modified;
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException) // Does not exists
                {
                    entity.State = EntityState.Added;
                    try
                    {
                        await ctx.SaveChangesAsync();
                    }
                    catch(DbUpdateException) // the Wallet does not exists in the DB
                    {
                        await SetWalletInfo(walletId, new WalletBlobInfo());
                        await ctx.SaveChangesAsync();
                    }
                }
            }
        }
    }
}
