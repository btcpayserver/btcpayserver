using BTCPayServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Models;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class SettingsRepository
    {
        private ApplicationDbContextFactory _ContextFactory;
        public SettingsRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory;
        }

        public async Task<T> GetSettingAsync<T>()
        {
            var name = typeof(T).FullName;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var data = await ctx.Settings.Where(s => s.Id == name).FirstOrDefaultAsync();
                if (data == null)
                    return default(T);
                return Deserialize<T>(data.Value);
            }
        }

        public async Task UpdateSetting<T>(T obj)
        {
            var name = obj.GetType().FullName;
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
        }

        private T Deserialize<T>(string value)
        {
            return JsonConvert.DeserializeObject<T>(value);
        }

        private string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
    }
}
