using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;

namespace BTCPayServer.HostedServices
{
    public class DynamicDnsHostedService : BaseAsyncService
    {
        public DynamicDnsHostedService(IHttpClientFactory httpClientFactory, SettingsRepository settingsRepository)
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

        TimeSpan Period = TimeSpan.FromMinutes(60);
        async Task UpdateRecord()
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
            {
                var settings = await SettingsRepository.GetSettingAsync<DynamicDnsSettings>();
                if (settings?.Enabled is true && (settings.LastUpdated is null ||
                                         (DateTimeOffset.UtcNow - settings.LastUpdated) > Period))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
                    try
                    {
                        var errorMessage = await settings.SendUpdateRequest(HttpClientFactory.CreateClient());
                        if (errorMessage == null)
                        {
                            Logs.PayServer.LogWarning($"Dynamic DNS service is enabled but the request to the provider failed: {errorMessage}");
                        }
                        else
                        {
                            Logs.PayServer.LogInformation("Dynamic DNS service successfully refresh the DNS record");
                        }
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                    }
                }
            }
            using (var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
            {
                var delay = Task.Delay(Period, delayCancel.Token);
                var changed = SettingsRepository.WaitSettingsChanged<DynamicDnsSettings>(Cancellation);
                await Task.WhenAny(delay, changed);
                delayCancel.Cancel();
            }
        }
    }
}
