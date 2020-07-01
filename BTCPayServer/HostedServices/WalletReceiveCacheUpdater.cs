using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Hosting;
using NBXplorer;

namespace BTCPayServer.HostedServices
{
    public class WalletReceiveCacheUpdater : IHostedService
    {
        private readonly EventAggregator _EventAggregator;
        private readonly WalletReceiveStateService _WalletReceiveStateService;

        private readonly CompositeDisposable _Leases = new CompositeDisposable();

        public WalletReceiveCacheUpdater(EventAggregator eventAggregator,
            WalletReceiveStateService walletReceiveStateService)
        {
            _EventAggregator = eventAggregator;
            _WalletReceiveStateService = walletReceiveStateService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Leases.Add(_EventAggregator.Subscribe<WalletChangedEvent>(evt =>
                _WalletReceiveStateService.Remove(evt.WalletId)));

            _Leases.Add(_EventAggregator.Subscribe<NewOnChainTransactionEvent>(evt =>
            {
                var matching = _WalletReceiveStateService
                    .GetByDerivation(evt.CryptoCode, evt.NewTransactionEvent.DerivationStrategy).Where(pair =>
                        evt.NewTransactionEvent.Outputs.Any(output => output.ScriptPubKey == pair.Value.ScriptPubKey));

                foreach (var keyValuePair in matching)
                {
                    _WalletReceiveStateService.Remove(keyValuePair.Key);
                }
            }));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Leases.Dispose();
            return Task.CompletedTask;
        }
    }
}
