using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.HostedServices;

namespace BTCPayServer.Services
{
    public class NBXSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly NBXplorerDashboard _nbXplorerDashboard;

        public NBXSyncSummaryProvider(NBXplorerDashboard nbXplorerDashboard)
        {
            _nbXplorerDashboard = nbXplorerDashboard;
        }

        public bool AllAvailable()
        {
            return _nbXplorerDashboard.IsFullySynched();
        }

        public string Partial { get; } = "Bitcoin/NBXSyncSummary";
    }
}
