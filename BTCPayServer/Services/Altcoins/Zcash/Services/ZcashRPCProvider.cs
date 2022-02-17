#if ALTCOINS
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using BTCPayServer.Services.Altcoins.Zcash.RPC;
using BTCPayServer.Services.Altcoins.Zcash.RPC.Models;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Zcash.Services
{
    public class ZcashRPCProvider
    {
        private readonly ZcashLikeConfiguration _ZcashLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, ZcashLikeSummary> _summaries =
            new ConcurrentDictionary<string, ZcashLikeSummary>();

        public ConcurrentDictionary<string, ZcashLikeSummary> Summaries => _summaries;

        public ZcashRPCProvider(ZcashLikeConfiguration ZcashLikeConfiguration, EventAggregator eventAggregator, IHttpClientFactory httpClientFactory)
        {
            _ZcashLikeConfiguration = ZcashLikeConfiguration;
            _eventAggregator = eventAggregator;
            DaemonRpcClients =
                _ZcashLikeConfiguration.ZcashLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, "", "", httpClientFactory.CreateClient()));
            WalletRpcClients =
                _ZcashLikeConfiguration.ZcashLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.InternalWalletRpcUri, "", "", httpClientFactory.CreateClient()));
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(ZcashLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task<ZcashLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient) ||
                !WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }

            var summary = new ZcashLikeSummary();
            try
            {
                var daemonResult =
                    await daemonRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, SyncInfoResponse>("sync_info",
                        JsonRpcClient.NoRequestModel.Instance);

                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = daemonResult.Height >= summary.TargetHeight && summary.CurrentHeight > 0;
                summary.UpdatedAt = DateTime.Now;
                summary.DaemonAvailable = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                summary.DaemonAvailable = false;
            }

            try
            {
                var walletResult =
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);

                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            var changed = !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            _summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new ZcashDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }


        public class ZcashDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public ZcashLikeSummary Summary { get; set; }
        }

        public class ZcashLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public bool WalletAvailable { get; set; }

            public override String ToString() { return String.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}", Synced, CurrentHeight, TargetHeight, WalletHeight, DaemonAvailable, WalletAvailable); }
        }
    }
}
#endif
