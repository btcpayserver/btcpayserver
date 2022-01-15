using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Custodian
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

        public async Task<CustodianAccountData> Find(string id, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            using var context = _ContextFactory.CreateContext();
            var result = await context.CustodianAccount.Include(x => x.StoreData)
                .Where(data =>
                    string.IsNullOrEmpty(userId) ||
                    (data.StoreData != null && data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)))
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return result;
        }

    }

}
