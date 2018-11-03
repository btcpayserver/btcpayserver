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
using System.Threading;

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

            IReadOnlyCollection<TaskCompletionSource<bool>> value;
            lock (_Subscriptions)
            {
                if(_Subscriptions.TryGetValue(typeof(T), out value))
                {
                    _Subscriptions.Remove(typeof(T));
                }
            }
            if(value != null)
            {
                foreach(var v in value)
                {
                    v.TrySetResult(true);
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

        MultiValueDictionary<Type, TaskCompletionSource<bool>> _Subscriptions = new MultiValueDictionary<Type, TaskCompletionSource<bool>>();
        public async Task WaitSettingsChanged<T>(CancellationToken cancellation)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellation.Register(() =>
             {
                 try
                 {
                     tcs.TrySetCanceled();
                 }
                 catch { }
             }))
            {
                lock (_Subscriptions)
                {
                    _Subscriptions.Add(typeof(T), tcs);
                }
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                tcs.Task.ContinueWith(_ =>
                 {
                     lock (_Subscriptions)
                     {
                         _Subscriptions.Remove(typeof(T), tcs);
                     }
                 }, TaskScheduler.Default);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await tcs.Task;
            }
        }
    }
}
