using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Migrations;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Services.Stores
{
    public class StoreRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public StoreRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<StoreData> FindStore(string storeId)
        {
            if (storeId == null)
                return null;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var result = await ctx.FindAsync<StoreData>(storeId).ConfigureAwait(false);
                return result;
            }
        }

        public async Task<StoreData> FindStore(string storeId, string userId)
        {
            if (userId == null)
                throw new ArgumentNullException(nameof(userId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                return (await ctx
                    .UserStore
                    .Where(us => us.ApplicationUserId == userId && us.StoreDataId == storeId)
                    .Select(us => new
                    {
                        Store = us.StoreData,
                        Role = us.Role
                    }).ToArrayAsync())
                    .Select(us =>
                    {
                        us.Store.Role = us.Role;
                        return us.Store;
                    }).FirstOrDefault();
            }
        }

        public class StoreUser
        {
            public string Id { get; set; }
            public string Email { get; set; }
            public string Role { get; set; }
        }
        public async Task<StoreUser[]> GetStoreUsers(string storeId)
        {
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx
                    .UserStore
                    .Where(u => u.StoreDataId == storeId)
                    .Select(u => new StoreUser()
                    {
                        Id = u.ApplicationUserId,
                        Email = u.ApplicationUser.Email,
                        Role = u.Role
                    }).ToArrayAsync();
            }
        }

        public async Task<StoreData[]> GetStoresByUserId(string userId, IEnumerable<string> storeIds = null)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return (await ctx.UserStore
                    .Where(u => u.ApplicationUserId == userId && (storeIds == null || storeIds.Contains(u.StoreDataId)))
                    .Select(u => new { u.StoreData, u.Role })
                    .ToArrayAsync())
                    .Select(u =>
                    {
                        u.StoreData.Role = u.Role;
                        return u.StoreData;
                    }).ToArray();
            }
        }

        public async Task<bool> AddStoreUser(string storeId, string userId, string role)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var userStore = new UserStore() { StoreDataId = storeId, ApplicationUserId = userId, Role = role };
                ctx.UserStore.Add(userStore);
                try
                {
                    await ctx.SaveChangesAsync();
                    return true;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    return false;
                }
            }
        }

        public async Task CleanUnreachableStores()
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                if (!ctx.Database.SupportDropForeignKey())
                    return;
                foreach (var store in await ctx.Stores.Where(s => !s.UserStores.Where(u => u.Role == StoreRoles.Owner).Any()).ToArrayAsync())
                {
                    ctx.Stores.Remove(store);
                }
                await ctx.SaveChangesAsync();
            }
        }

        public async Task RemoveStoreUser(string storeId, string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var userStore = new UserStore() { StoreDataId = storeId, ApplicationUserId = userId };
                ctx.UserStore.Add(userStore);
                ctx.Entry<UserStore>(userStore).State = Microsoft.EntityFrameworkCore.EntityState.Deleted;
                await ctx.SaveChangesAsync();

            }
            await DeleteStoreIfOrphan(storeId);
        }

        private async Task DeleteStoreIfOrphan(string storeId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                if (ctx.Database.SupportDropForeignKey())
                {
                    if (!await ctx.UserStore.Where(u => u.StoreDataId == storeId && u.Role == StoreRoles.Owner).AnyAsync())
                    {
                        var store = await ctx.Stores.FindAsync(storeId);
                        if (store != null)
                        {
                            ctx.Stores.Remove(store);
                            await ctx.SaveChangesAsync();
                        }
                    }
                }
            }
        }

        public async Task CreateStore(string ownerId, StoreData storeData)
        {
            if (!string.IsNullOrEmpty(storeData.Id))
                throw new ArgumentException("id should be empty", nameof(storeData.StoreName));
            if (string.IsNullOrEmpty(storeData.StoreName))
                throw new ArgumentException("name should not be empty", nameof(storeData.StoreName));
            if (ownerId == null)
                throw new ArgumentNullException(nameof(ownerId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                storeData.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32));
                var userStore = new UserStore
                {
                    StoreDataId = storeData.Id,
                    ApplicationUserId = ownerId,
                    Role = StoreRoles.Owner
                };
                ctx.Add(storeData);
                ctx.Add(userStore);
                await ctx.SaveChangesAsync();
            }
        }

        public async Task<StoreData> CreateStore(string ownerId, string name)
        {
            var store = new StoreData() { StoreName = name };
            await CreateStore(ownerId, store);
            return store;
        }

        public async Task RemoveStore(string storeId, string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var storeUser = await ctx.UserStore.AsQueryable().FirstOrDefaultAsync(o => o.StoreDataId == storeId && o.ApplicationUserId == userId);
                if (storeUser == null)
                    return;
                ctx.UserStore.Remove(storeUser);
                await ctx.SaveChangesAsync();
            }
            await DeleteStoreIfOrphan(storeId);
        }

        public async Task UpdateStore(StoreData store)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var existing = await ctx.FindAsync<StoreData>(store.Id);
                ctx.Entry(existing).CurrentValues.SetValues(store);
                await ctx.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteStore(string storeId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                if (!ctx.Database.SupportDropForeignKey())
                    return false;
                var store = await ctx.Stores.FindAsync(storeId);
                if (store == null)
                    return false;
                ctx.Stores.Remove(store);
                await ctx.SaveChangesAsync();
                return true;
            }
        }

        public bool CanDeleteStores()
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return ctx.Database.SupportDropForeignKey();
            }
        }
    }
}
