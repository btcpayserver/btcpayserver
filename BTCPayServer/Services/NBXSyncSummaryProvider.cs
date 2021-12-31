using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using NBXplorer.Models;

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
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _nbXplorerDashboard.GetAll()
                .Where(summary => summary.Network.ShowSyncSummary)
                .Select(summary => new ServerInfoSyncStatusData2
                {
                    CryptoCode = summary.Network.CryptoCode,
                    NodeInformation = summary.Status.BitcoinStatus is BitcoinStatus s ? new ServerInfoNodeData()
                    {
                        Headers = s.Headers,
                        Blocks = s.Blocks,
                        VerificationProgress = s.VerificationProgress
                    } : null,
                    ChainHeight = summary.Status.ChainHeight,
                    SyncHeight = summary.Status.SyncHeight,
                    Available = summary.Status.IsFullySynched
                });
        }

        public class ServerInfoSyncStatusData2 : ServerInfoSyncStatusData, ISyncStatus
        {

        }
    }


}
