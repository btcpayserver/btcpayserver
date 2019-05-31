using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BTCPayServer.Monero.RPC;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroRPCProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        public ImmutableDictionary<string, MoneroDaemonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, MoneroWalletRpcClient> WalletRpcClients;
       
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

        public async Task<MoneroLikeSummary> GetSummary(string cryptoCode)
        {
            if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient))
            {
                return null;
            }
          
            if (!WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }
            
            var result = await daemonRpcClient.SyncInfo();
            var walletResult = await walletRpcClient.GetHeight();
            return new MoneroLikeSummary()
            {
                TargetHeight = result.TargetHeight?? result.Height,
                Available = !result.TargetHeight.HasValue || result.Height < result.TargetHeight,
                CurrentHeight = result.Height,
                UpdatedAt = DateTime.Now,
                WalletHeight = walletResult.Height
            };
        }


        public class MoneroLikeSummary
        {
            public bool Available { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
        }
    }

}
