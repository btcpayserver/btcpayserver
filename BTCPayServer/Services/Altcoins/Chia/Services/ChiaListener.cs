#if ALTCOINS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaListener : IHostedService
    {
        private readonly EventAggregator _eventAggregator;
        private readonly ChiaRPCProvider _chiaRpcProvider;
        private readonly ChiaLikeConfiguration _chiaLikeConfiguration;
        private readonly ChiaLikePaymentHandler _paymentHandler;
        private readonly ILogger<ChiaListener> _logger;
        private readonly CompositeDisposable _leases = new();
        private readonly Channel<Func<Task>> _requests = Channel.CreateUnbounded<Func<Task>>();
        private CancellationTokenSource _cts;

        public ChiaListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ChiaLikeConfiguration chiaLikeConfiguration,
            ChiaRPCProvider chiaRpcProvider,
            ChiaLikePaymentHandler paymentHandler,
            ILogger<ChiaListener> logger)
        {
            _eventAggregator = eventAggregator;
            _chiaLikeConfiguration = chiaLikeConfiguration;
            _chiaRpcProvider = chiaRpcProvider;
            _paymentHandler = paymentHandler;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_chiaLikeConfiguration.ChiaLikeConfigurationItems.Any())
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _leases.Add(_eventAggregator.Subscribe<ChiaRPCProvider.ChiaDaemonStateChange>(e =>
            {
                if (_chiaRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ = _paymentHandler.UpdateAnyPendingChiaLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));
            using var cts = new CancellationTokenSource(5000);

            _ = WorkThroughQueue(_cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkThroughQueue(CancellationToken token)
        {
            while (await _requests.Reader.WaitToReadAsync(token) && _requests.Reader.TryRead(out var action))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await action.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError($"error with action item {e}");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _leases.Dispose();
            _cts?.Cancel();
            return Task.CompletedTask;
        }
    }
}
#endif
