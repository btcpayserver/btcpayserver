using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services
{
    public class WalletRepository
    {
        private ApplicationDbContextFactory _ContextFactory;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public WalletRepository(ApplicationDbContextFactory contextFactory, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public async Task<(int Total, IEnumerable<WalletData> Wallets)> GetWallets(WalletQuery query, CancellationToken cancellationToken = default)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var queryable = context.Wallets.Include(data => data.StoreWalletDatas).AsQueryable();

                if (query.IncludeTransactionInfo)
                {
                    queryable = queryable.Include(data => data.WalletTransactions);
                }

                if (query.StoreId != null && query.StoreId.Any())
                {
                    queryable = queryable.Where(i =>  i.StoreWalletDatas.Select(data => data.StoreDataId).Any(s => query.StoreId.Contains(s)) );
                }

                if (query.UserId != null && query.UserId.Any())
                {
                    queryable = queryable.Where(i => query.UserId.Contains( i.ApplicationUserId ));
                }

                if (query.PaymentTypes != null && query.PaymentTypes.Any())
                {
                    queryable = queryable.Where(i => query.PaymentTypes.Contains( i.PaymentType ));
                }

                if (query.WalletId != null && query.WalletId.Any())
                {
                    queryable = queryable.Where(i => query.WalletId.Contains( i.Id ));
                }

                var total = await queryable.CountAsync(cancellationToken);

                if (query.Skip.HasValue)
                {
                    queryable = queryable.Skip(query.Skip.Value);
                }

                if (query.Count.HasValue)
                {
                    queryable = queryable.Take(query.Count.Value);
                }
                return (total, await queryable.ToArrayAsync(cancellationToken));
            }
        }

        public async Task<WalletData> GetWallet(string id, bool includeTransactionInfo = false)
        {
            var res = await GetWallets(new WalletQuery() {WalletId = new[] {id}, IncludeTransactionInfo = includeTransactionInfo});
            return res.Wallets.FirstOrDefault();
        }

        public async Task<WalletData> CreateOrUpdateWallet(WalletData entity)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                if (string.IsNullOrEmpty(entity.Id))
                {
                    entity.Id = Guid.NewGuid().ToString();
                    await context.Wallets.AddAsync(entity);
                }
                else
                {
                    context.Wallets.Update(entity);
                }

                await context.SaveChangesAsync();
                return entity;
            }
        }


        

        public async Task SetWalletTransactionInfo(string walletId, string transactionId,
            WalletTransactionInfo walletTransactionInfo)
        {
            if (walletId == null)
                throw new ArgumentNullException(nameof(walletId));
            if (transactionId == null)
                throw new ArgumentNullException(nameof(transactionId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                var walletData =
                    new WalletTransactionData() {WalletDataId = walletId, TransactionId = transactionId};
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
                    await ctx.SaveChangesAsync();
                }
            }
        }
        
        public class WalletQuery
        {
            public bool IncludeTransactionInfo { get; set; }
            public string[] StoreId { get; set; }
            public string[] WalletId { get; set; }
            public string[] UserId { get; set; }
            public string[] PaymentTypes { get; set; }
            public int? Skip { get; set; }
            public int? Count { get; set; }
        }
    }
}
