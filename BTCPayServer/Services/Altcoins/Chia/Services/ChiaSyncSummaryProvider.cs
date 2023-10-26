#if ALTCOINS
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly ChiaRPCProvider _ChiaRpcProvider;

        public ChiaSyncSummaryProvider(ChiaRPCProvider ChiaRpcProvider)
        {
            _ChiaRpcProvider = ChiaRpcProvider;
        }

        public bool AllAvailable()
        {
            return _ChiaRpcProvider.Summaries.All(pair => pair.Value.WalletAvailable);
        }

        public string Partial { get; } = "Chia/ChiaSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _ChiaRpcProvider.Summaries.Select(pair => new ChiaSyncStatus()
            {
                Summary = pair.Value, CryptoCode = pair.Key
            });
        }
    }

    public class ChiaSyncStatus: SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get
            {
                return Summary?.WalletAvailable ?? false;
            }
        }

        public ChiaRPCProvider.ChiaLikeSummary Summary { get; set; }
    }
}
#endif
