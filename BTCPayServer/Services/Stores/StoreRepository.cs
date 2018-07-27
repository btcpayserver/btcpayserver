using BTCPayServer.Data;
using BTCPayServer.Models;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Stores
{
    public class StoreRepository
    {
        private ApplicationDbContextFactory _ContextFactory;
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
                return await ctx.FindAsync<StoreData>(storeId).ConfigureAwait(false);
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
#pragma warning disable CS0612 // Type or member is obsolete
                        us.Store.Role = us.Role;
#pragma warning restore CS0612 // Type or member is obsolete
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

        public async Task<StoreData[]> GetStoresByUserId(string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return (await ctx.UserStore
                    .Where(u => u.ApplicationUserId == userId)
                    .Select(u => new { u.StoreData, u.Role })
                    .ToArrayAsync())
                    .Select(u =>
                    {
#pragma warning disable CS0612 // Type or member is obsolete
                        u.StoreData.Role = u.Role;
#pragma warning restore CS0612 // Type or member is obsolete
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
                catch (DbUpdateException)
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
                foreach (var store in await ctx.Stores.Where(s => s.UserStores.Where(u => u.Role == StoreRoles.Owner).Count() == 0).ToArrayAsync())
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
                ctx.Entry<UserStore>(userStore).State = EntityState.Deleted;
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
                    if (await ctx.UserStore.Where(u => u.StoreDataId == storeId && u.Role == StoreRoles.Owner).CountAsync() == 0)
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

        public async Task<StoreData> CreateStore(string ownerId, string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name should not be empty", nameof(name));
            using (var ctx = _ContextFactory.CreateContext())
            {
                StoreData store = new StoreData
                {
                    Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32)),
                    StoreName = name,
                    SpeedPolicy = Invoices.SpeedPolicy.MediumSpeed
                };
                var userStore = new UserStore
                {
                    StoreDataId = store.Id,
                    ApplicationUserId = ownerId,
                    Role = "Owner"
                };
                ctx.Add(store);
                ctx.Add(userStore);
                await ctx.SaveChangesAsync().ConfigureAwait(false);
                return store;
            }
        }

        public async Task RemoveStore(string storeId, string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var storeUser = await ctx.UserStore.FirstOrDefaultAsync(o => o.StoreDataId == storeId && o.ApplicationUserId == userId);
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
