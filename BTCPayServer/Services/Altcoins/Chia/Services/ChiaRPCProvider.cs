#if ALTCOINS
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Common.Altcoins.Chia.RPC;
using BTCPayServer.Common.Altcoins.Chia.RPC.Models;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.RPC;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Chia.Services
{
    public class ChiaRPCProvider
    {
        private readonly ChiaLikeConfiguration _ChiaLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, ChiaLikeSummary> _summaries =
            new ConcurrentDictionary<string, ChiaLikeSummary>();

        public ConcurrentDictionary<string, ChiaLikeSummary> Summaries => _summaries;

        public ChiaRPCProvider(ChiaLikeConfiguration ChiaLikeConfiguration, EventAggregator eventAggregator)
        {
            _ChiaLikeConfiguration = ChiaLikeConfiguration;
            _eventAggregator = eventAggregator;

            WalletRpcClients =
                _ChiaLikeConfiguration.ChiaLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.WalletEndpoint.Uri, pair.Value.WalletEndpoint.CertPath,
                        pair.Value.WalletEndpoint.KeyPath));
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(ChiaLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task<ChiaLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }

            var summary = new ChiaLikeSummary();
            try
            {
                var syncStatus =
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetSyncStatusResponse>(
                        "get_sync_status", JsonRpcClient.NoRequestModel.Instance);

                var walletResult =
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightInfoResponse>(
                        "get_height_info", JsonRpcClient.NoRequestModel.Instance);

                summary.Synced = syncStatus.Synced;
                summary.WalletHeight = walletResult.Height;
                summary.UpdatedAt = DateTime.Now;
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
                _eventAggregator.Publish(new ChiaDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }


        public class ChiaDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public ChiaLikeSummary Summary { get; set; }
        }

        public class ChiaLikeSummary
        {
            public bool Synced { get; set; }
            public long WalletHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool WalletAvailable { get; set; }

            public override String ToString()
            {
                return String.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}", Synced, WalletHeight, WalletAvailable);
            }
        }
    }
}
#endif
