#if ALTCOINS
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaLikeTransactionUpdaterHostedService : IHostedService
    {
        private readonly ChiaRPCProvider _ChiaRpcProvider;
        private readonly ChiaLikeConfiguration _ChiaLikeConfiguration;
        private readonly ChiaLikePaymentHandler _paymentHandler;
        private readonly ILogger<ChiaLikeTransactionUpdaterHostedService> _logger;
        private CancellationTokenSource _Cts;

        public ChiaLikeTransactionUpdaterHostedService(ChiaRPCProvider ChiaRpcProvider,
            ChiaLikeConfiguration ChiaLikeConfiguration, ILogger<ChiaLikeTransactionUpdaterHostedService> logger,
            ChiaLikePaymentHandler paymentHandler,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            PaymentService paymentService
        )
        {
            _ChiaRpcProvider = ChiaRpcProvider;
            _ChiaLikeConfiguration = ChiaLikeConfiguration;
            _logger = logger;
            _paymentHandler = paymentHandler;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var chiaLikeConfigurationItem in _ChiaLikeConfiguration.ChiaLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, chiaLikeConfigurationItem.Key);
            }

            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            _logger.LogInformation($"Starting listening Chia-like transactions ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        if (_ChiaRpcProvider.IsAvailable(cryptoCode))
                        {
                            await _paymentHandler.UpdateAnyPendingChiaLikePayment(cryptoCode);
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
                        }
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        _logger.LogError(ex, $"Unhandled exception in transaction updater ({cryptoCode})");
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
#endif
