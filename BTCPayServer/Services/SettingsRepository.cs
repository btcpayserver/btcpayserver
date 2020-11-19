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

        public async Task<T> GetSettingAsync<T>(string name = null)
        {
            name ??= typeof(T).FullName;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var data = await ctx.Settings.Where(s => s.Id == name).FirstOrDefaultAsync();
                if (data == null)
                    return default(T);
                return Deserialize<T>(data.Value);
            }
        }

        public async Task UpdateSetting<T>(T obj, string name = null)
        {
            name ??= obj.GetType().FullName;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var settings = new SettingData();
                settings.Id = name;
                settings.Value = Serialize(obj);
                ctx.Attach(settings);
                ctx.Entry(settings).State = EntityState.Modified;
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

        private T Deserialize<T>(string value)
        {
            return JsonConvert.DeserializeObject<T>(value);
        }

        private string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public async Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default)
        {
            return (await _EventAggregator.WaitNext<SettingsChanged<T>>(cancellationToken)).Settings;
        }
    }
}
