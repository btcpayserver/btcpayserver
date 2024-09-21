using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.RPC;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Services.Altcoins.Monero.UI;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class MoneroRPCProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        private readonly IHttpClientFactory _httpClientFactory;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, MoneroLikeSummary> _summaries =
            new ConcurrentDictionary<string, MoneroLikeSummary>();

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries => _summaries;

        public MoneroRPCProvider(MoneroLikeConfiguration moneroLikeConfiguration, EventAggregator eventAggregator, IHttpClientFactory httpClientFactory)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _eventAggregator = eventAggregator;
            _httpClientFactory = httpClientFactory;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password, httpClientFactory.CreateClient($"{pair.Key}client")));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.InternalWalletRpcUri, "", "", httpClientFactory.CreateClient($"{pair.Key}client")));
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(MoneroLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        // Method to update DaemonRpcClients and then update the summary
        public async Task UpdateDaemonRpcClientsAndSummaryAsync(string cryptoCode, DaemonParams daemonParams)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();

            if (_moneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode, out MoneroLikeConfigurationItem configItem))
            {
                // Update the configuration with new parameters
                configItem.DaemonRpcUri = daemonParams.address;
                configItem.Username = daemonParams.username;
                configItem.Password = daemonParams.password;
                
                // Reinitialize the DaemonRpcClients based on the updated configuration
                DaemonRpcClients = _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password, _httpClientFactory.CreateClient($"{pair.Key}client")));

                // Update the summary after updating the DaemonRpcClient
                await UpdateSummary(cryptoCode);
            }
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
                var daemonResult =
                    await daemonRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetInfoResponse>("get_info",
                        JsonRpcClient.NoRequestModel.Instance);
                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = !daemonResult.BusySyncing;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch
            {
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
                _eventAggregator.Publish(new MoneroDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
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
