#nullable enable
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class SettingsRepository : ISettingsRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _EventAggregator;

        public SettingsRepository(ApplicationDbContextFactory contextFactory, EventAggregator eventAggregator)
        {
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
        }

        public async Task<T?> GetSettingAsync<T>(string? name = null) where T : class
        {
            name ??= typeof(T).FullName ?? string.Empty;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var data = await ctx.Settings.Where(s => s.Id == name).FirstOrDefaultAsync();
                if (data == null)
                    return default(T);
                return Deserialize<T>(data.Value);
            }
        }
        public async Task UpdateSetting<T>(T obj, string? name = null) where T : class
        {
            using (var ctx = _ContextFactory.CreateContext())
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
            _EventAggregator.Publish(new SettingsChanged<T>()
            {
                Settings = obj
            });
        }

        public SettingData UpdateSettingInContext<T>(ApplicationDbContext ctx, T obj, string? name = null) where T : class
        {
            name ??= obj.GetType().FullName ?? string.Empty;
            var settings = new SettingData();
            settings.Id = name;
            settings.Value = Serialize(obj);

            ctx.Attach(settings);
            ctx.Entry(settings).State = EntityState.Modified;
            
            return settings;
        }

        private T Deserialize<T>(string value)
        {
            return JsonConvert.DeserializeObject<T>(value);
        }

        private string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public async Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class
        {
            return (await _EventAggregator.WaitNext<SettingsChanged<T>>(cancellationToken)).Settings;
        }
    }
}
