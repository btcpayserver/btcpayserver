using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Hosting
{
    public class BlockExplorerLinkStartupTask : IStartupTask
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly TransactionLinkProviders _transactionLinksProviders;
        private readonly IConfiguration _configuration;

        public BlockExplorerLinkStartupTask(
            BTCPayNetworkProvider btcPayNetworkProvider,
            TransactionLinkProviders transactionLinksProviders,
            IConfiguration configuration)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _transactionLinksProviders = transactionLinksProviders;
            _configuration = configuration;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var blockExplorerLink = _configuration["blockexplorerlink"];
            if (!string.IsNullOrEmpty(blockExplorerLink))
            {
                foreach (var prov in _transactionLinksProviders.Values)
                    prov.OverrideBlockExplorerLink = blockExplorerLink;
            }
            await _transactionLinksProviders.RefreshTransactionLinkTemplates();
        }
    }
}
