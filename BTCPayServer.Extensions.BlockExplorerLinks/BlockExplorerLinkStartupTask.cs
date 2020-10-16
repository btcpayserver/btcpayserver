using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Hosting;
using BTCPayServer.Services;

namespace BTCPayServer.Extensions.BlockExplorerLinks
{
    public class BlockExplorerLinkStartupTask : IStartupTask
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public BlockExplorerLinkStartupTask(ISettingsRepository settingsRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _settingsRepository = settingsRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _settingsRepository.GetSettingAsync<ExplorerLinkSettings>();
            if (settings?.NetworkLinks?.Any() is true)
            {
                foreach (var item in settings.NetworkLinks)
                {
                    _btcPayNetworkProvider.GetNetwork(item.Key).BlockExplorerLink = item.Value;
                }
            }
        }
    }
}
