using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services.Altcoins.Zcash.Services
{
    public class ZcashLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly ZcashRPCProvider _ZcashRpcProvider;
        private readonly ZcashLikeConfiguration _ZcashLikeConfiguration;
        private CancellationTokenSource _Cts;

        public Logs Logs { get; }

        public ZcashLikeSummaryUpdaterHostedService(ZcashRPCProvider ZcashRpcProvider, ZcashLikeConfiguration ZcashLikeConfiguration, Logs logs)
        {
            _ZcashRpcProvider = ZcashRpcProvider;
            _ZcashLikeConfiguration = ZcashLikeConfiguration;
            Logs = logs;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var ZcashLikeConfigurationItem in _ZcashLikeConfiguration.ZcashLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, ZcashLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Zcash-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _ZcashRpcProvider.UpdateSummary(cryptoCode);
                        if (_ZcashRpcProvider.IsAvailable(cryptoCode))
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                        }
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts?.Cancel();
            return Task.CompletedTask;
        }
    }
}
