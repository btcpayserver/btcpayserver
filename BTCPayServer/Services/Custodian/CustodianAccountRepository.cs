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
        private readonly ApplicationDbContextFactory _contextFactory;

        public CustodianAccountRepository(ApplicationDbContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<CustodianAccountData> CreateOrUpdate(CustodianAccountData entity)
        {
            await using var context = _contextFactory.CreateContext();
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
            await using var context = _contextFactory.CreateContext();
            var key = await context.CustodianAccount.SingleOrDefaultAsync(data => data.Id == id && data.StoreId == storeId);
            if (key == null)
                return false;
            context.CustodianAccount.Remove(key);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<CustodianAccountData[]> FindByStoreId(string storeId,
            CancellationToken cancellationToken = default)
        {
            if (storeId is null)
                throw new ArgumentNullException(nameof(storeId));
            await using var context = _contextFactory.CreateContext();
            IQueryable<CustodianAccountData> query = context.CustodianAccount
                .Where(ca => ca.StoreId == storeId);

            var data = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
            return data;
        }

        public async Task<CustodianAccountData> FindById(string storeId, string accountId)
        {
            await using var context = _contextFactory.CreateContext();
            IQueryable<CustodianAccountData> query = context.CustodianAccount
                .Where(ca => ca.StoreId == storeId && ca.Id == accountId);

            var custodianAccountData = (await query.ToListAsync()).FirstOrDefault();
            return custodianAccountData;
        }
    }
}
