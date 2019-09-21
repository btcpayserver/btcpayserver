using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MoneroRPC.NET;
using NBitcoin;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroRPCProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, MoneroDaemonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, MoneroWalletRpcClient> WalletRpcClients;
        private ConcurrentDictionary<string, MoneroLikeSummary> _summaries = new ConcurrentDictionary<string, MoneroLikeSummary>();

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries => _summaries;

        public MoneroRPCProvider(MoneroLikeConfiguration moneroLikeConfiguration, EventAggregator eventAggregator)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _eventAggregator = eventAggregator;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroDaemonRpcClient(pair.Value.DaemonRpcUri));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroWalletRpcClient(pair.Value.InternalWalletRpcUri, "", ""));
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(MoneroLikeSummary summary)
        {
            return  summary.Synced &&
                    summary.WalletAvailable;
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
                summary.Synced = !daemonResult.TargetHeight.HasValue || daemonResult.Height >= daemonResult.TargetHeight;
                summary.CurrentHeight = daemonResult.Height;
                summary.UpdatedAt = DateTime.Now;
                summary.DaemonAvailable = true;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }

            try
            {
                var walletResult = await walletRpcClient.GetHeight();

                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            var changed =  !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);
            
            _summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new MoneroDaemonStateChange()
                {
                    Summary = summary,
                    CryptoCode = cryptoCode
                });
            }
            return summary;
        }


        public class MoneroDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public MoneroLikeSummary Summary { get; set; }
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
