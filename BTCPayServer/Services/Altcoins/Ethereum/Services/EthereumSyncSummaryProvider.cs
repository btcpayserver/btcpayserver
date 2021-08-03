#if ALTCOINS
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services.Altcoins.Ethereum.Services
{
    public class EthereumSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly EthereumService _ethereumService;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public EthereumSyncSummaryProvider(EthereumService ethereumService, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _ethereumService = ethereumService;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public bool AllAvailable()
        {
            return _ethereumService.IsAllAvailable();
        }

        public string Partial { get; } = "Ethereum/ETHSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _btcPayNetworkProvider
                .GetAll()
                .OfType<EthereumBTCPayNetwork>()
                .Where(network => !(network is ERC20BTCPayNetwork))
                .Select(network => network.CryptoCode).Select(network => new SyncStatus()
            {
                CryptoCode = network, 
                Available = _ethereumService.IsAvailable(network, out _)
            });
        }

        public class SyncStatus : ISyncStatus
        {
            public string CryptoCode { get; set; }
            public bool Available { get; set; }
        }
    }
}
#endif
