#if ALTCOINS
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Matic.Services
{
    public class MaticSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly MaticService _maticService;

        public MaticSyncSummaryProvider(MaticService maticService)
        {
            _maticService = maticService;
        }

        public bool AllAvailable()
        {
            return _maticService.IsAllAvailable();
        }

        public string Partial { get; } = "Matic/MaticSyncSummary";
    }
}
#endif
