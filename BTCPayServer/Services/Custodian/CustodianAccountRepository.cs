using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Custodian.Client
{
    public class CustodianAccountRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public CustodianAccountRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory;
        }

        public async Task<CustodianAccountData> CreateOrUpdate(CustodianAccountData entity)
        {
            using var context = _ContextFactory.CreateContext();
            if (string.IsNullOrEmpty(entity.Id))
            {
                entity.Id = Guid.NewGuid().ToString();
                await context.CustodianAccount.AddAsync(entity);
            }
            else
            {
                context.CustodianAccount.Update(entity);
            }

            await context.SaveChangesAsync();
            return entity;
        }

        public async Task<bool> Remove(string id, string storeId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var key = await EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(context.CustodianAccount,
                    data => data.Id == id && data.StoreId == storeId);
                if (key == null)
                    return false;
                context.CustodianAccount.Remove(key);
                await context.SaveChangesAsync();
            }
            return true;
        }

        public async Task<CustodianAccountData[]> FindByStoreId(string storeId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return null;
            }

            using var context = _ContextFactory.CreateContext();
            IQueryable<CustodianAccountData> query = context.CustodianAccount
                .Where(ca => ca.StoreId == storeId);
            //.SelectMany(c => c.StoreData.Invoices);

            var data = await query.ToArrayAsync().ConfigureAwait(false);
            return data;
        }

        public async Task<CustodianAccountData> FindById(string accountId)
        {
            using var context = _ContextFactory.CreateContext();
            IQueryable<CustodianAccountData> query = context.CustodianAccount
                .Where(ca => ca.Id == accountId);

            var custodianAccountData = (await query.ToListAsync()).FirstOrDefault();
            if (custodianAccountData == null)
                return null;

            return custodianAccountData;
        }
    }
}
