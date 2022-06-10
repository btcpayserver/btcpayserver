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

namespace BTCPayServer.Services;

public class StoreSettingsRepository : IStoreSettingsRepository
{
    private readonly ApplicationDbContextFactory _ContextFactory;
    private readonly EventAggregator _EventAggregator;
    private readonly IMemoryCache _memoryCache;

    public StoreSettingsRepository(ApplicationDbContextFactory contextFactory, EventAggregator eventAggregator, IMemoryCache memoryCache)
    {
        _ContextFactory = contextFactory;
        _EventAggregator = eventAggregator;
        _memoryCache = memoryCache;
    }
    public StoreSettingData? UpdateSettingInContext<T>(ApplicationDbContext ctx, string storeId,string name, T obj) where T : class
    {
        _memoryCache.Remove(GetCacheKey(storeId, name));
        if (obj is null)
        {
            ctx.StoreSettings.RemoveRange(ctx.StoreSettings.Where(data => data.Name == name && data.StoreId == storeId));
            return null;
        }
        else
        {
            var settings = new StoreSettingData() { Name = name, StoreId = storeId, Value = Serialize(obj) };
            ctx.Attach(settings);
            ctx.Entry(settings).State = EntityState.Modified;
            return settings;
        }
    }

    private T? Deserialize<T>(string value) where T : class
    {
        return JsonConvert.DeserializeObject<T>(value);
    }

    private string Serialize<T>(T obj)
    {
        return JsonConvert.SerializeObject(obj);
    }

    private string GetCacheKey(string storeId, string name)
    {
        return $"{nameof(StoreSettingsRepository)}_{storeId}_{name}";
    }

    public async Task<T?> GetSettingAsync<T>(string storeId, string name, bool cache) where T : class
    {
        async Task<T?> Get()
        {
            await using var ctx = _ContextFactory.CreateContext();
            var data = await ctx.StoreSettings.Where(s => s.Name == name && s.StoreId == storeId).FirstOrDefaultAsync();
            return data == null ? default : Deserialize<T>(data.Value);
        }

        if (cache)
        {
            return await _memoryCache.GetOrCreateAsync(GetCacheKey(storeId, name), async _ => await Get());
        }

        return await Get();

    }

    public async Task UpdateSetting<T>(string storeId, string name, T obj) where T : class
    {
        await using (var ctx = _ContextFactory.CreateContext())
        {
            var settings = UpdateSettingInContext(ctx, storeId, name, obj);
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if(settings is not null)
                {
                    ctx.Entry(settings).State = EntityState.Added;
                    await ctx.SaveChangesAsync();
                }
            }
        }
        _memoryCache.Set(GetCacheKey(storeId, name), obj);
        _EventAggregator.Publish(new SettingsChanged<T>()
        {
            StoreId = storeId,
            Settings = obj,
            SettingsName = name
        });
    }


    public async Task<T> WaitSettingsChanged<T>(string storeId, string name,
        CancellationToken cancellationToken = default) where T : class
    {
        return (await _EventAggregator.WaitNext<SettingsChanged<T>>(
            changed => changed.StoreId == storeId && changed.SettingsName == name, cancellationToken)).Settings;
    }
        
    public async Task InvalidateCacheForStore(ApplicationDbContext ctx, string storeId)
    {
        var names = (await ctx.StoreSettings.Where(data => data.StoreId == storeId).Select(data => data.Name)
            .ToArrayAsync()).Select(s => GetCacheKey(s, storeId));
        foreach (string name in names)
        {
            _memoryCache.Remove(name);
        }
    }
}
