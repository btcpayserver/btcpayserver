#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Migrations;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Stores
{
    public class StoreRepository : IStoreRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public JsonSerializerSettings SerializerSettings { get; }

        public ApplicationDbContext CreateDbContext()
        {
            return _ContextFactory.CreateContext();
        }
        public StoreRepository(ApplicationDbContextFactory contextFactory, JsonSerializerSettings serializerSettings)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            SerializerSettings = serializerSettings;
        }

        public async Task<StoreData?> FindStore(string storeId)
        {
            if (storeId == null)
                return null;
            await using var ctx = _ContextFactory.CreateContext();
            var result = await ctx.FindAsync<StoreData>(storeId).ConfigureAwait(false);
            return result;
        }

        public async Task<StoreData?> FindStore(string storeId, string userId)
        {
            ArgumentNullException.ThrowIfNull(userId);
            await using var ctx = _ContextFactory.CreateContext();
            return (await ctx
                    .UserStore
                    .Where(us => us.ApplicationUserId == userId && us.StoreDataId == storeId)
                    .Include(store => store.StoreData.UserStores)
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
#nullable disable
        public class StoreUser
        {
            public string Id { get; set; }
            public string Email { get; set; }
            public string Role { get; set; }
        }
#nullable enable
        public async Task<StoreUser[]> GetStoreUsers(string storeId)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _ContextFactory.CreateContext();
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

        public async Task<StoreData[]> GetStoresByUserId(string userId, IEnumerable<string>? storeIds = null)
        {
            using var ctx = _ContextFactory.CreateContext();
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

        public async Task<StoreData?> GetStoreByInvoiceId(string invoiceId)
        {
            await using var context = _ContextFactory.CreateContext();
            var matched = await context.Invoices.Include(data => data.StoreData)
                .SingleOrDefaultAsync(data => data.Id == invoiceId);
            return matched?.StoreData;
        }

        public async Task<bool> AddStoreUser(string storeId, string userId, string role)
        {
            using var ctx = _ContextFactory.CreateContext();
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

        public async Task CleanUnreachableStores()
        {
            using var ctx = _ContextFactory.CreateContext();
            if (!ctx.Database.SupportDropForeignKey())
                return;
            foreach (var store in await ctx.Stores.Where(s => !s.UserStores.Where(u => u.Role == StoreRoles.Owner).Any()).ToArrayAsync())
            {
                ctx.Stores.Remove(store);
            }
            await ctx.SaveChangesAsync();
        }

        public async Task<bool> RemoveStoreUser(string storeId, string userId)
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (!await ctx.UserStore.AnyAsync(store =>
                    store.StoreDataId == storeId && store.Role == StoreRoles.Owner &&
                    userId != store.ApplicationUserId)) return false;
            var userStore = new UserStore() { StoreDataId = storeId, ApplicationUserId = userId };
            ctx.UserStore.Add(userStore);
            ctx.Entry(userStore).State = EntityState.Deleted;
            await ctx.SaveChangesAsync();
            return true;

        }

        private async Task DeleteStoreIfOrphan(string storeId)
        {
            using var ctx = _ContextFactory.CreateContext();
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
        public async Task CreateStore(string ownerId, StoreData storeData)
        {
            if (!string.IsNullOrEmpty(storeData.Id))
                throw new ArgumentException("id should be empty", nameof(storeData.StoreName));
            if (string.IsNullOrEmpty(storeData.StoreName))
                throw new ArgumentException("name should not be empty", nameof(storeData.StoreName));
            ArgumentNullException.ThrowIfNull(ownerId);
            using var ctx = _ContextFactory.CreateContext();
            storeData.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32));
            var userStore = new UserStore
            {
                StoreDataId = storeData.Id,
                ApplicationUserId = ownerId,
                Role = StoreRoles.Owner,
            };

            ctx.Add(storeData);
            ctx.Add(userStore);
            await ctx.SaveChangesAsync();
        }

        public async Task<StoreData> CreateStore(string ownerId, string name, string defaultCurrency, string preferredExchange)
        {
            var store = new StoreData { StoreName = name };
            var blob = store.GetStoreBlob();
            blob.DefaultCurrency = defaultCurrency;
            blob.PreferredExchange = preferredExchange;
            store.SetStoreBlob(blob);
            
            await CreateStore(ownerId, store);
            return store;
        }

        public async Task<WebhookData[]> GetWebhooks(string storeId)
        {
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.StoreWebhooks
                            .Where(s => s.StoreId == storeId)
                            .Select(s => s.Webhook).ToArrayAsync();
        }

        public async Task<WebhookDeliveryData?> GetWebhookDelivery(string storeId, string webhookId, string deliveryId)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.StoreWebhooks
                .Where(d => d.StoreId == storeId && d.WebhookId == webhookId)
                .SelectMany(d => d.Webhook.Deliveries)
                .Where(d => d.Id == deliveryId)
                .FirstOrDefaultAsync();
        }

        public async Task AddWebhookDelivery(WebhookDeliveryData delivery)
        {
            using var ctx = _ContextFactory.CreateContext();
            ctx.WebhookDeliveries.Add(delivery);
            var invoiceWebhookDelivery = delivery.GetBlob().ReadRequestAs<InvoiceWebhookDeliveryData>();
            if (invoiceWebhookDelivery.InvoiceId != null)
            {
                ctx.InvoiceWebhookDeliveries.Add(new InvoiceWebhookDeliveryData()
                {
                    InvoiceId = invoiceWebhookDelivery.InvoiceId,
                    DeliveryId = delivery.Id
                });
            }
            await ctx.SaveChangesAsync();
        }

        public async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId, int? count)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _ContextFactory.CreateContext();
            IQueryable<WebhookDeliveryData> req = ctx.StoreWebhooks
                .Where(s => s.StoreId == storeId && s.WebhookId == webhookId)
                .SelectMany(s => s.Webhook.Deliveries)
                .OrderByDescending(s => s.Timestamp);
            if (count is int c)
                req = req.Take(c);
            return await req
                .ToArrayAsync();
        }

        public async Task<string> CreateWebhook(string storeId, WebhookBlob blob)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            ArgumentNullException.ThrowIfNull(blob);
            using var ctx = _ContextFactory.CreateContext();
            WebhookData data = new WebhookData();
            data.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
            if (string.IsNullOrEmpty(blob.Secret))
                blob.Secret = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
            data.SetBlob(blob);
            StoreWebhookData storeWebhook = new StoreWebhookData();
            storeWebhook.StoreId = storeId;
            storeWebhook.WebhookId = data.Id;
            ctx.StoreWebhooks.Add(storeWebhook);
            ctx.Webhooks.Add(data);
            await ctx.SaveChangesAsync();
            return data.Id;
        }

        public async Task<WebhookData?> GetWebhook(string storeId, string webhookId)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.StoreWebhooks
                .Where(s => s.StoreId == storeId && s.WebhookId == webhookId)
                .Select(s => s.Webhook)
                .FirstOrDefaultAsync();
        }
        public async Task<WebhookData?> GetWebhook(string webhookId)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.StoreWebhooks
                .Where(s => s.WebhookId == webhookId)
                .Select(s => s.Webhook)
                .FirstOrDefaultAsync();
        }
        public async Task DeleteWebhook(string storeId, string webhookId)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _ContextFactory.CreateContext();
            var hook = await ctx.StoreWebhooks
                .Where(s => s.StoreId == storeId && s.WebhookId == webhookId)
                .Select(s => s.Webhook)
                .FirstOrDefaultAsync();
            if (hook is null)
                return;
            ctx.Webhooks.Remove(hook);
            await ctx.SaveChangesAsync();
        }

        public async Task UpdateWebhook(string storeId, string webhookId, WebhookBlob webhookBlob)
        {
            ArgumentNullException.ThrowIfNull(webhookId);
            ArgumentNullException.ThrowIfNull(storeId);
            ArgumentNullException.ThrowIfNull(webhookBlob);
            using var ctx = _ContextFactory.CreateContext();
            var hook = await ctx.StoreWebhooks
                .Where(s => s.StoreId == storeId && s.WebhookId == webhookId)
                .Select(s => s.Webhook)
                .FirstOrDefaultAsync();
            if (hook is null)
                return;
            hook.SetBlob(webhookBlob);
            await ctx.SaveChangesAsync();
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
            using var ctx = _ContextFactory.CreateContext();
            var existing = await ctx.FindAsync<StoreData>(store.Id);
            if (existing is not null)
            {
                ctx.Entry(existing).CurrentValues.SetValues(store);
                await ctx.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteStore(string storeId)
        {
            int retry = 0;
            using var ctx = _ContextFactory.CreateContext();
            if (!ctx.Database.SupportDropForeignKey())
                return false;
            var store = await ctx.Stores.FindAsync(storeId);
            if (store == null)
                return false;
            var webhooks = await ctx.StoreWebhooks
                .Where(o => o.StoreId == storeId)
                .Select(o => o.Webhook)
                .ToArrayAsync();
            foreach (var w in webhooks)
                ctx.Webhooks.Remove(w);
            ctx.Stores.Remove(store);
            retry:
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDeadlock(ex) && retry < 5)
            {
                await Task.Delay(100);
                retry++;
                goto retry;
            }
            return true;
        }

        private T? Deserialize<T>(string value) where T : class
        {
            return JsonConvert.DeserializeObject<T>(value, SerializerSettings);
        }

        private string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, SerializerSettings);
        }
        public async Task<T?> GetSettingAsync<T>(string storeId, string name) where T : class
        {
            await using var ctx = _ContextFactory.CreateContext();
            var data = await ctx.StoreSettings.Where(s => s.Name == name && s.StoreId == storeId).FirstOrDefaultAsync();
            return data == null ? default : this.Deserialize<T>(data.Value);

        }

        public async Task UpdateSetting<T>(string storeId, string name, T obj) where T : class
        {
            await using var ctx = _ContextFactory.CreateContext();
            StoreSettingData? settings = null;
            if (obj is null)
            {
                ctx.StoreSettings.RemoveRange(ctx.StoreSettings.Where(data => data.Name == name && data.StoreId == storeId));
            }
            else
            {
                settings = new StoreSettingData() { Name = name, StoreId = storeId, Value = Serialize(obj) };
                ctx.Attach(settings);
                ctx.Entry(settings).State = EntityState.Modified;
            }
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (settings is not null)
                {
                    ctx.Entry(settings).State = EntityState.Added;
                    await ctx.SaveChangesAsync();
                }
            }
        }

        private static bool IsDeadlock(DbUpdateException ex)
        {
            return ex.InnerException is Npgsql.PostgresException postgres && postgres.SqlState == "40P01";
        }

        public bool CanDeleteStores()
        {
            using var ctx = _ContextFactory.CreateContext();
            return ctx.Database.SupportDropForeignKey();
        }
    }
}
