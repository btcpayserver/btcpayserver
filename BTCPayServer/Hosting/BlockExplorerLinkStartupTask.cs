using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;

namespace BTCPayServer.Hosting
{
    public class BlockExplorerLinkStartupTask : IStartupTask
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public BlockExplorerLinkStartupTask(SettingsRepository settingsRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _settingsRepository = settingsRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _settingsRepository.GetSettingAsync<PoliciesSettings>();
            if (settings?.BlockExplorerLinks?.Any() is true)
            {
                foreach (var item in settings.BlockExplorerLinks)
                {
                    var network = _btcPayNetworkProvider.GetNetwork(item.CryptoCode);
                    if (network is null)
                    {
                        continue;;
                    }
                    network.BlockExplorerLink = item.Link;
                }
            }
        }
    }
}
