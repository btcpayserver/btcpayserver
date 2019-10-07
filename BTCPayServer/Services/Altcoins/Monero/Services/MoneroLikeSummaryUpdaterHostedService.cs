using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class MoneroLikeSummaryUpdaterHostedService: IHostedService
    {
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private CancellationTokenSource _Cts;
        public MoneroLikeSummaryUpdaterHostedService(MoneroRPCProvider moneroRpcProvider, MoneroLikeConfiguration moneroLikeConfiguration)
        {
            _MoneroRpcProvider = moneroRpcProvider;
            _moneroLikeConfiguration = moneroLikeConfiguration;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var moneroLikeConfigurationItem in _moneroLikeConfiguration.MoneroLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, moneroLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }
        
        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Monero-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _MoneroRpcProvider.UpdateSummary(cryptoCode);
                        if (_MoneroRpcProvider.IsAvailable(cryptoCode))
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
