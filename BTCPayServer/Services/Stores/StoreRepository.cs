#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
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
        private readonly EventAggregator _eventAggregator;
        private readonly SettingsRepository _settingsRepository;

        public JsonSerializerSettings SerializerSettings { get; }

        public ApplicationDbContext CreateDbContext()
        {
            return _ContextFactory.CreateContext();
        }
        public StoreRepository(ApplicationDbContextFactory contextFactory, JsonSerializerSettings serializerSettings, EventAggregator eventAggregator, SettingsRepository settingsRepository)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _eventAggregator = eventAggregator;
            _settingsRepository = settingsRepository;
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
            return await ctx
                .UserStore
                .Where(us => us.ApplicationUserId == userId && us.StoreDataId == storeId)
                .Include(store => store.StoreData.UserStores)
                .ThenInclude(store => store.StoreRole)
                .Select(us => us.StoreData).FirstOrDefaultAsync();
        }
#nullable disable
        public class StoreUser
        {
            public string Id { get; set; }
            public string Email { get; set; }
            public StoreRole StoreRole { get; set; }
        }

        public class StoreRole
        {
            public PermissionSet ToPermissionSet(string storeId)
            {
                return new PermissionSet(Permissions
                    .Select(s => Permission.TryCreatePermission(s, storeId, out var permission) ? permission : null)
                    .Where(s => s != null).ToArray());
            }
            public string Role { get; set; }
            public List<string> Permissions { get; set; }
            public bool IsServerRole { get; set; }
            public string Id { get; set; }
            public bool? IsUsed { get; set; }
        }
#nullable enable
        public async Task<StoreRole[]> GetStoreRoles(string? storeId, bool includeUsers = false, bool storeOnly = false)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var query = ctx.StoreRoles.Where(u => (storeOnly && u.StoreDataId == storeId) || (!storeOnly && (u.StoreDataId == null || u.StoreDataId == storeId)));
            if (includeUsers)
            {
                query = query.Include(u => u.Users);
            }
            return (await query.ToArrayAsync()).Select(role => ToStoreRole(role)).ToArray();
        }

        public async Task<StoreRoleId> GetDefaultRole()
        {
            var r = (await _settingsRepository.GetSettingAsync<PoliciesSettings>())?.DefaultRole;
            if (r is not null)
                return new StoreRoleId(r);
            return StoreRoleId.Owner;
        }
        public async Task SetDefaultRole(string role)
        {
            var s = (await _settingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            s.DefaultRole = role;
            await _settingsRepository.UpdateSetting(s);
        }

        public async Task<StoreRole?> GetStoreRole(StoreRoleId role, bool includeUsers = false)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var query = ctx.StoreRoles.AsQueryable();
            if (includeUsers)
            {
                query = query.Include(u => u.Users);
            }
            var match = await query.SingleOrDefaultAsync(r => r.Id == role.Id);
            if (match == null)
                return null;
            if (match.StoreDataId != role.StoreId)
                // Should never happen because the roleId include the storeId
                throw new InvalidOperationException("Bug 03991: This should never happen");
            return ToStoreRole(match);
        }

        public async Task<string?> RemoveStoreRole(StoreRoleId role)
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (await GetDefaultRole() == role)
            {
                return "Cannot remove the default role";
            }

            var match = await ctx.StoreRoles.FindAsync(role.Id);
            if (match != null && match.StoreDataId == role.StoreId)
            {
                if (role.StoreId is null && match.Permissions.Contains(Policies.CanModifyStoreSettings) &&
                    await ctx.StoreRoles.CountAsync(role =>
                        role.StoreDataId == null && role.Permissions.Contains(Policies.CanModifyStoreSettings)) == 1)
                    return "This is the last role that allows to modify store settings, you cannot remove it";
                ctx.StoreRoles.Remove(match);
                await ctx.SaveChangesAsync();
                return null;
            }

            return "Role not found";
        }

        public async Task<StoreRole?> AddOrUpdateStoreRole(StoreRoleId role, List<string> policies)
        {
            policies = policies.Where(s => Policies.IsValidPolicy(s) && Policies.IsStorePolicy(s)).ToList();
            await using var ctx = _ContextFactory.CreateContext();
            Data.StoreRole? match = await ctx.StoreRoles.FindAsync(role.Id);
            if (match is null)
            {
                match = new Data.StoreRole() { Id = role.Id, StoreDataId = role.StoreId, Role = role.Role };
                ctx.StoreRoles.Add(match);
            }
            match.Permissions = policies;
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return null;
            }
            return ToStoreRole(match);
        }


        public async Task<StoreUser[]> GetStoreUsers(string storeId)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            await using var ctx = _ContextFactory.CreateContext();
            return (await
                ctx
                    .UserStore
                    .Where(u => u.StoreDataId == storeId)
                    .Include(u => u.StoreRole)
                    .Select(u => new
                    {
                        Id = u.ApplicationUserId,
                        u.ApplicationUser.Email,
                        u.StoreRole
                    }).ToArrayAsync()).Select(arg => new StoreUser()
                    {
                        StoreRole = ToStoreRole(arg.StoreRole),
                        Id = arg.Id,
                        Email = arg.Email
                    }).ToArray();
        }

        public static StoreRole ToStoreRole(Data.StoreRole storeRole)
        {
            return new StoreRole()
            {
                Id = storeRole.Id,
                Role = storeRole.Role,
                Permissions = storeRole.Permissions,
                IsServerRole = storeRole.StoreDataId == null,
                IsUsed = storeRole.Users?.Any()
            };
        }

        public async Task<StoreData[]> GetStoresByUserId(string userId, IEnumerable<string>? storeIds = null)
        {
            await using var ctx = _ContextFactory.CreateContext();
            return (await ctx.UserStore
                .Where(u => u.ApplicationUserId == userId && (storeIds == null || storeIds.Contains(u.StoreDataId)))
                .Include(store => store.StoreData)
                .ThenInclude(data => data.UserStores)
                .ThenInclude(data => data.StoreRole)
                .Select(store => store.StoreData)
                .ToArrayAsync());
        }

        public async Task<StoreData?> GetStoreByInvoiceId(string invoiceId)
        {
            await using var context = _ContextFactory.CreateContext();
            var matched = await context.Invoices.Include(data => data.StoreData)
                .SingleOrDefaultAsync(data => data.Id == invoiceId);
            return matched?.StoreData;
        }

        /// <summary>
        /// `role` can be passed in two format:
        /// STOREID::ROLE or ROLE.
        /// If the first case, this method make sure the storeId is same as <paramref name="storeId"/>.
        /// In the second case, we interprete ROLE as a server level roleId first, then if it does not exist, check if there is a store level role.
        /// </summary>
        /// <param name="storeId"></param>
        /// <param name="role"></param>
        /// <returns></returns>
        public async Task<StoreRoleId?> ResolveStoreRoleId(string? storeId, string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return null;
            if (storeId?.Contains("::", StringComparison.OrdinalIgnoreCase) is true)
                return null;
            var roleId = StoreRoleId.Parse(role);
            if (roleId.StoreId != null && roleId.StoreId != storeId)
                return null;
            if ((await GetStoreRole(roleId)) != null)
                return roleId;
            if (string.IsNullOrEmpty(storeId))
                return null;
            if (roleId.IsServerRole)
                roleId = new StoreRoleId(storeId, role);
            if ((await GetStoreRole(roleId)) != null)
                return roleId;
            return null;
        }

        public async Task<bool> AddStoreUser(string storeId, string userId, StoreRoleId? roleId = null)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            AssertStoreRoleIfNeeded(storeId, roleId);
            roleId ??= await GetDefaultRole();
            await using var ctx = _ContextFactory.CreateContext();
            var userStore = new UserStore() { StoreDataId = storeId, ApplicationUserId = userId, StoreRoleId = roleId.Id };
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

        static void AssertStoreRoleIfNeeded(string storeId, StoreRoleId? roleId)
        {
            if (roleId?.StoreId != null && storeId != roleId.StoreId)
                throw new ArgumentException("The roleId doesn't belong to this storeId", nameof(roleId));
        }

        public async Task CleanUnreachableStores()
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (!ctx.Database.SupportDropForeignKey())
                return;
            var events = new List<Events.StoreRemovedEvent>();
            foreach (var store in await ctx.Stores.Include(data => data.UserStores)
                         .ThenInclude(store => store.StoreRole).Where(s =>
                             s.UserStores.All(u => !u.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings)))
                         .ToArrayAsync())
            {
                ctx.Stores.Remove(store);
                events.Add(new Events.StoreRemovedEvent(store.Id));
            }
            await ctx.SaveChangesAsync();
            events.ForEach(e => _eventAggregator.Publish(e));
        }

        public async Task<bool> RemoveStoreUser(string storeId, string userId)
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (!await ctx.UserStore.Include(store => store.StoreRole).AnyAsync(store =>
                    store.StoreDataId == storeId && store.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings) &&
                    userId != store.ApplicationUserId))
                return false;
            var userStore = new UserStore() { StoreDataId = storeId, ApplicationUserId = userId };
            ctx.UserStore.Add(userStore);
            ctx.Entry(userStore).State = EntityState.Deleted;
            await ctx.SaveChangesAsync();
            return true;

        }

        private async Task DeleteStoreIfOrphan(string storeId)
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (ctx.Database.SupportDropForeignKey())
            {
                if (!await ctx.UserStore.Where(u => u.StoreDataId == storeId && u.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings)).AnyAsync())
                {
                    var store = await ctx.Stores.FindAsync(storeId);
                    if (store != null)
                    {
                        ctx.Stores.Remove(store);
                        await ctx.SaveChangesAsync();
                        _eventAggregator.Publish(new StoreRemovedEvent(store.Id));
                    }
                }
            }
        }
        public async Task CreateStore(string ownerId, StoreData storeData, StoreRoleId? roleId = null)
        {
            if (!string.IsNullOrEmpty(storeData.Id))
                throw new ArgumentException("id should be empty", nameof(storeData.StoreName));
            if (string.IsNullOrEmpty(storeData.StoreName))
                throw new ArgumentException("name should not be empty", nameof(storeData.StoreName));
            ArgumentNullException.ThrowIfNull(ownerId);
            AssertStoreRoleIfNeeded(storeData.Id, roleId);
            await using var ctx = _ContextFactory.CreateContext();
            storeData.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32));
            roleId ??= await GetDefaultRole();

            var userStore = new UserStore
            {
                StoreDataId = storeData.Id,
                ApplicationUserId = ownerId,
                StoreRoleId = roleId.Id,
            };

            ctx.Add(storeData);
            ctx.Add(userStore);
            await ctx.SaveChangesAsync();
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
            if (count is { } c)
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

            if (string.IsNullOrEmpty(webhookBlob.Secret))
            {
                webhookBlob.Secret = hook.GetBlob().Secret;
            }
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

        public async Task<Dictionary<string, T?>> GetSettingsAsync<T>(string name) where T : class
        {
            await using var ctx = _ContextFactory.CreateContext();
            var data = await ctx.StoreSettings.Where(s => s.Name == name).ToDictionaryAsync(settingData => settingData.StoreId);
            return data.ToDictionary(pair => pair.Key, pair => Deserialize<T>(pair.Value.Value));
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

    public record StoreRoleId
    {
        public static StoreRoleId Parse(string str)
        {
            var i = str.IndexOf("::");
            string? storeId = null;
            string role;
            if (i == -1)
            {
                role = str;
                return new StoreRoleId(role);
            }
            else
            {
                storeId = str[0..i];
                role = str[(i + 2)..];
                return new StoreRoleId(storeId, role);
            }
        }
        public StoreRoleId(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Role shouldn't be null or empty", nameof(role));
            if (role.Contains("::", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Role shouldn't contains '::'", nameof(role));
            Role = role;
        }
        public StoreRoleId(string storeId, string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Role shouldn't be null or empty", nameof(role));
            if (string.IsNullOrWhiteSpace(storeId))
                throw new ArgumentException("StoreId shouldn't be null or empty", nameof(storeId));
            if (role.Contains("::", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Role shouldn't contains '::'", nameof(role));
            if (storeId.Contains("::", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("StoreId shouldn't contains '::'", nameof(storeId));
            StoreId = storeId;
            Role = role;
        }

        public static StoreRoleId Owner { get; } = new StoreRoleId("Owner");
        public static StoreRoleId Guest { get; } = new StoreRoleId("Guest");
        public string? StoreId { get; }
        public string Role { get; }
        public string Id
        {
            get
            {
                if (StoreId is null)
                    return Role;
                return $"{StoreId}::{Role}";
            }
        }

        public bool IsServerRole => StoreId is null;
        public override string ToString()
        {
            return Id;
        }
    }
}
