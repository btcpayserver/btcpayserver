using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices
{
    public class DynamicDnsHostedService : BaseAsyncService
    {
        public DynamicDnsHostedService(IHttpClientFactory httpClientFactory, SettingsRepository settingsRepository, Logs logs) : base(logs)
        {
            HttpClientFactory = httpClientFactory;
            SettingsRepository = settingsRepository;
        }

        public IHttpClientFactory HttpClientFactory { get; }
        public SettingsRepository SettingsRepository { get; }

        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                CreateLoopTask(UpdateRecord)
            };
        }

        readonly TimeSpan Period = TimeSpan.FromMinutes(60);
        async Task UpdateRecord()
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken))
            {
                var settings = await SettingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
                foreach (var service in settings.Services)
                {
                    if (service?.Enabled is true && (service.LastUpdated is null ||
                                             (DateTimeOffset.UtcNow - service.LastUpdated) > Period))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
                        try
                        {
                            var errorMessage = await service.SendUpdateRequest(HttpClientFactory.CreateClient());
                            if (errorMessage == null)
                            {
                                Logs.PayServer.LogInformation("Dynamic DNS service successfully refresh the DNS record");
                                service.LastUpdated = DateTimeOffset.UtcNow;
                                await SettingsRepository.UpdateSetting(settings);
                            }
                            else
                            {
                                Logs.PayServer.LogWarning($"Dynamic DNS service is enabled but the request to the provider failed: {errorMessage}");
                            }
                        }
                        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                        {
                        }
                    }
                }
            }
            using var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            var delay = Task.Delay(Period, delayCancel.Token);
            var changed = SettingsRepository.WaitSettingsChanged<DynamicDnsSettings>(CancellationToken);
            await Task.WhenAny(delay, changed);
            delayCancel.Cancel();
        }
    }
}
