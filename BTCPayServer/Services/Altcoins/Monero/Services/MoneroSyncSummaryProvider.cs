#if ALTCOINS
using System.Linq;
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class MoneroSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public MoneroSyncSummaryProvider(MoneroRPCProvider moneroRpcProvider)
        {
            _moneroRpcProvider = moneroRpcProvider;
        }

        public bool AllAvailable()
        {
            return _moneroRpcProvider.Summaries.All(pair => pair.Value.WalletAvailable);
        }

        public string Partial { get; } = "Monero/MoneroSyncSummary";
    }
}
#endif
