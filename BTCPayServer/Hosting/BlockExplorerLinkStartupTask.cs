using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
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
                SetLinkOnNetworks(settings.BlockExplorerLinks, _btcPayNetworkProvider);
            }
        }

        public static void SetLinkOnNetworks(List<PoliciesSettings.BlockExplorerOverrideItem> links,
            BTCPayNetworkProvider networkProvider)
        {
            var networks = networkProvider.GetAll();
            foreach (var network in networks)
            {
                var overrideLink = links.SingleOrDefault(item =>
                    item.CryptoCode.Equals(network.CryptoCode, StringComparison.InvariantCultureIgnoreCase));
                network.BlockExplorerLink = overrideLink?.Link ?? network.BlockExplorerLinkDefault;

            }
        }
    }
}
