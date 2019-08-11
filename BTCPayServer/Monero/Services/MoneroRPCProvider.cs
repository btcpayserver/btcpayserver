using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Monero.RPC;
using NBitcoin;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroRPCProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        public ImmutableDictionary<string, MoneroDaemonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, MoneroWalletRpcClient> WalletRpcClients;
        private ConcurrentDictionary<string, MoneroLikeSummary> _summaries = new ConcurrentDictionary<string, MoneroLikeSummary>();

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries => _summaries;

        public MoneroRPCProvider(MoneroLikeConfiguration moneroLikeConfiguration)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroDaemonRpcClient(pair.Value.DaemonRpcUri));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroWalletRpcClient(pair.Value.DaemonRpcUri, null, null));
        }

        public bool IsAvailable(string cryptocode)
        {
            return _summaries.ContainsKey(cryptocode.ToUpperInvariant()) &&
                   _summaries[cryptocode.ToUpperInvariant()].Synced &&
                   _summaries[cryptocode.ToUpperInvariant()].WalletAvailable;
        }

        public async Task<MoneroLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient) ||
                !WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }

            var summary = new MoneroLikeSummary();
            try
            {
                var daemonResult = await daemonRpcClient.SyncInfo();
                summary.TargetHeight = daemonResult.TargetHeight ?? daemonResult.Height;
                summary.Synced = !daemonResult.TargetHeight.HasValue || daemonResult.Height < daemonResult.TargetHeight;
                summary.CurrentHeight = daemonResult.Height;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }

            try
            {
                var walletResult = await walletRpcClient.GetHeight();

                summary.WalletHeight = walletResult.Height;
            }
            catch
            {
                summary.WalletAvailable = false;
            }
            _summaries.AddOrReplace(cryptoCode, summary);
            return summary;
        }


        public class MoneroLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}
