#if ALTCOINS
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly ChiaRPCProvider _ChiaRpcProvider;
        private readonly ChiaLikeConfiguration _ChiaLikeConfiguration;
        private CancellationTokenSource _Cts;

        public Logs Logs { get; }

        public ChiaLikeSummaryUpdaterHostedService(ChiaRPCProvider ChiaRpcProvider, ChiaLikeConfiguration ChiaLikeConfiguration, Logs logs)
        {
            _ChiaRpcProvider = ChiaRpcProvider;
            _ChiaLikeConfiguration = ChiaLikeConfiguration;
            Logs = logs;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var ChiaLikeConfigurationItem in _ChiaLikeConfiguration.ChiaLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, ChiaLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Chia-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _ChiaRpcProvider.UpdateSummary(cryptoCode);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
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
#endif
