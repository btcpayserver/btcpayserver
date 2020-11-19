#if ALTCOINS
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services.Altcoins.Ethereum.Services
{
    public class EthereumSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly EthereumService _ethereumService;

        public EthereumSyncSummaryProvider(EthereumService ethereumService)
        {
            _ethereumService = ethereumService;
        }

        public bool AllAvailable()
        {
            return _ethereumService.IsAllAvailable();
        }

        public string Partial { get; } = "Ethereum/ETHSyncSummary";
    }
}
#endif
