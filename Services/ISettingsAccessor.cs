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
    class SettingsAccessor<T> : ISettingsAccessor<T>, IStartupTask, IHostedService where T : class, new()
    {
        T? _Settings;
        public T Settings => _Settings ?? throw new InvalidOperationException($"Settings {typeof(T)} not yet initialized");

        public EventAggregator Aggregator { get; }
        public ISettingsRepository SettingsRepository { get; }

        IDisposable? disposable;

        public SettingsAccessor(EventAggregator aggregator, ISettingsRepository settings)
        {
            Aggregator = aggregator;
            SettingsRepository = settings;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_Settings != null)
                return;
            _Settings = await SettingsRepository.GetSettingAsync<T>() ?? new T();
            disposable = Aggregator.Subscribe<SettingsChanged<T>>(v => _Settings = Clone(v.Settings));
        }

        private T Clone(T settings)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(settings))!;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            disposable?.Dispose();
            return Task.CompletedTask;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await StartAsync(cancellationToken);
        }
    }
}
