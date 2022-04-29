#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class SettingsRepository : ISettingsRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _EventAggregator;
        private readonly IMemoryCache _memoryCache;

        public SettingsRepository(ApplicationDbContextFactory contextFactory, EventAggregator eventAggregator,
            IMemoryCache memoryCache)
        {
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
            _memoryCache = memoryCache;
        }

        public async Task<T?> GetSettingAsync<T>(string? name, string? storeId) where T : class
        {
            name ??= typeof(T).FullName ?? string.Empty;
            return await _memoryCache.GetOrCreateAsync(GetCacheKey(name, storeId), async _ =>
            {
                await using var ctx = _ContextFactory.CreateContext();
                var data = await ctx.Settings.Where(s => s.Name == name && (storeId == null || s.StoreId.Equals(storeId))).FirstOrDefaultAsync();
                return data == null ? default : Deserialize<T>(data.Value);
            });
        }

        public Task<T?> GetSettingAsync<T>(string? name = null) where T : class
        {
            return GetSettingAsync<T>(name, null);
        }

        public async Task UpdateSetting<T>(T obj, string? name, string? storeId) where T : class
        {
            name ??= typeof(T).FullName ?? string.Empty;
            await using (var ctx = _ContextFactory.CreateContext())
            {
                var settings = UpdateSettingInContext<T>(ctx, obj, name, storeId);
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    ctx.Entry(settings).State = EntityState.Added;
                    await ctx.SaveChangesAsync();
                }
            }

            _memoryCache.Set(GetCacheKey(name, storeId), obj);
            _EventAggregator.Publish(new SettingsChanged<T>() {Settings = obj, SettingsName = name, StoreId = storeId});
        }

        public Task UpdateSetting<T>(T obj, string? name = null) where T : class
        {
            return UpdateSetting(obj, name, null);
        }

        public SettingData UpdateSettingInContext<T>(ApplicationDbContext ctx, T obj, string? name = null,
            string? storeId = null)
            where T : class
        {
            name ??= obj.GetType().FullName ?? string.Empty;
            var key = GetCacheKey(name, storeId);
            _memoryCache.Remove(key);
            var settings = new SettingData {Id = key, Name = name, Value = Serialize(obj), StoreId = storeId};
            ctx.Attach(settings);
            ctx.Entry(settings).State = EntityState.Modified;
            return settings;
        }

        private T? Deserialize<T>(string value) where T : class
        {
            return JsonConvert.DeserializeObject<T>(value);
        }

        private string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        private static string GetCacheKey(string name, string? storeId)
        {
            return $"{nameof(SettingsRepository)}_{name}_{storeId}";
        }

        public async Task InvalidateCacheForStore(ApplicationDbContext ctx, string storeId)
        {
            var names = (await ctx.Settings.Where(data => data.StoreId == storeId).Select(data => data.Name)
                .ToArrayAsync()).Select(s => GetCacheKey(s, storeId));
            foreach (string name in names)
            {
                _memoryCache.Remove(name);
            }
        }

        public async Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class
        {
            return await WaitSettingsChanged<T>(null, null, cancellationToken);
        }

        public async Task<T> WaitSettingsChanged<T>(string? name = null, string? storeId = null,
            CancellationToken cancellationToken = default) where T : class
        {
            return (await _EventAggregator.WaitNext<SettingsChanged<T>>(
                changed => name is null || changed.SettingsName == name, cancellationToken)).Settings;
        }
    }
}
