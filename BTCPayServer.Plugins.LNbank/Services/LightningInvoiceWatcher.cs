using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class LightningInvoiceWatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<LightningInvoiceWatcher> _logger;

        public LightningInvoiceWatcher(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<LightningInvoiceWatcher> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(nameof(LightningInvoiceWatcher) + " starting");

            while (!cancellationToken.IsCancellationRequested)
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();

                    var transactions = await walletService.GetPendingTransactions();
                    var list = transactions.ToList();
                    int count = list.Count;

                    if (count > 0)
                    {
                        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                        _logger.LogDebug(nameof(LightningInvoiceWatcher) + " processing " + count + " transactions");

                        await Task.WhenAll(list.Select(transaction => walletService.CheckPendingTransaction(transaction, cancellationToken)));
                    }

                    await Task.Delay(5_000, cancellationToken);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(nameof(LightningInvoiceWatcher) + " stopping");

            await Task.CompletedTask;
        }
    }
}
