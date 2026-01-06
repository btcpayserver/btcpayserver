#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Dapper;
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
            name ??= KeyNameByType(typeof(T));
            var data = await _memoryCache.GetOrCreateAsync(GetCacheKey(name), async entry =>
            {
                await using var ctx = _ContextFactory.CreateContext();
                var data = await ctx.Settings.Where(s => s.Id == name).FirstOrDefaultAsync();
                return data?.Value;
            });
            return data is string ? Deserialize<T>(data) : null;
        }

        public async Task<Dictionary<Type, object?>> LoadAllSettings((Type Type, string? KeyName)[] settings)
        {
            var settingsByName = settings
                .ToDictionary(s => s.KeyName ?? KeyNameByType(s.Type));
            await using var ctx = _ContextFactory.CreateContext();
            var rows = await ctx.Database.GetDbConnection()
                .QueryAsync<(string Name, string? Value)>("""
                                    SELECT val, s."Value"
                                    FROM unnest(@names) val
                                    LEFT JOIN "Settings" s ON s."Id" = val
                                    """, new{ names = settingsByName.Keys.ToArray() });
            var result = new Dictionary<Type, object?>();
            foreach (var row in rows)
            {
                var type = settingsByName[row.Name].Type;
                var obj = row.Value is null ? null : Deserialize(type, row.Value);
                result.Add(type, obj);
                _memoryCache.GetOrCreate(GetCacheKey(row.Name), e => row.Value);
            }
            return result;
        }

        public async Task UpdateSetting<T>(T obj, string? name = null) where T : class
        {
            name ??=  KeyNameByType(obj.GetType());
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
            _memoryCache.Remove(GetCacheKey(name));
            _EventAggregator.Publish(new SettingsChanged<T>()
            {
                Settings = obj,
                SettingsName = name
            });
        }

        public static string KeyNameByType(Type type)
        => type.FullName ?? string.Empty;

        public SettingData UpdateSettingInContext<T>(ApplicationDbContext ctx, T obj, string? name = null) where T : class
        {
            name ??= KeyNameByType(obj.GetType());
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
        private object? Deserialize(Type type, string value)
        {
            return JsonConvert.DeserializeObject(value, type);
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
