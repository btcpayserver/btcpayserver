#nullable enable
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

        public SettingsRepository(ApplicationDbContextFactory contextFactory, EventAggregator eventAggregator, IMemoryCache memoryCache)
        {
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
            _memoryCache = memoryCache;
        }

        public async Task<T?> GetSettingAsync<T>(string? name = null) where T : class
        {
            name ??= typeof(T).FullName ?? string.Empty;
            return await _memoryCache.GetOrCreateAsync(GetCacheKey(name), async entry =>
            {
                await using var ctx = _ContextFactory.CreateContext();
                var data = await ctx.Settings.Where(s => s.Id == name).FirstOrDefaultAsync();
                return data == null ? default : Deserialize<T>(data.Value);
            });
        }
        public async Task UpdateSetting<T>(T obj, string? name = null) where T : class
        {
            name ??= typeof(T).FullName ?? string.Empty;
            await using (var ctx = _ContextFactory.CreateContext())
            {
                var settings = UpdateSettingInContext<T>(ctx, obj, name);
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
            _memoryCache.Set(GetCacheKey(name), obj);
            _EventAggregator.Publish(new SettingsChanged<T>()
            {
                Settings = obj,
                SettingsName = name
            });
        }

        public SettingData UpdateSettingInContext<T>(ApplicationDbContext ctx, T obj, string? name = null) where T : class
        {
            name ??= obj.GetType().FullName ?? string.Empty;
            _memoryCache.Remove(GetCacheKey(name));
            var settings = new SettingData { Id = name, Value = Serialize(obj) };
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

        private string GetCacheKey(string name)
        {
            return $"{nameof(SettingsRepository)}_{name}";
        }

        public async Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class
        {
            return (await _EventAggregator.WaitNext<SettingsChanged<T>>(cancellationToken)).Settings;
        }
    }
}
