#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Events;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public interface ISettingsAccessor<T>
    {
        T Settings { get; }
    }

    abstract class SettingsAccessor
    {
        public abstract void SetSetting(object? setting);
    }

    class SettingsAccessor<T> : SettingsAccessor, ISettingsAccessor<T> where T : class, new()
    {
        T? _settings;
        public T Settings => _settings ?? throw new InvalidOperationException($"Settings {typeof(T)} not yet initialized");

        private T Clone(T settings)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(settings))!;
        }


        public override void SetSetting(object? setting)
        {
            _settings = setting is not null ? Clone((T)setting) : new T();
        }
    }
}
