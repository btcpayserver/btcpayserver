using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Services.Fees
{
    public class MempoolSpaceFeeProvider : IFeeProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly bool _testnet;

        public MempoolSpaceFeeProvider(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache,
            bool testnet = false)
        {
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
            _testnet = testnet;
        }

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            var result = await GetFeeRatesAsync();

            return result.TryGetValue(blockTarget, out var feeRate)
                ? feeRate
                :
                //try get the closest one
                result[result.Keys.MinBy(key => Math.Abs(key - blockTarget))];
        }

        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public async Task<Dictionary<int, FeeRate>> GetFeeRatesAsync()
        {
            try
            {
                await _semaphoreSlim.WaitAsync();
                return await _memoryCache.GetOrCreateAsync(nameof(MempoolSpaceFeeProvider),
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                        using var client = _httpClientFactory.CreateClient(nameof(MempoolSpaceFeeProvider));
                        var result =
                            await client.GetAsync(
                                $"https://mempool.space{(_testnet ? "/testnet" : "")}/api/v1/fees/recommended");
                        result.EnsureSuccessStatusCode();
                        var recommendedFees = await result.Content.ReadAsAsync<Dictionary<string, decimal>>();

                        entry.Value = recommendedFees.ToDictionary(pair =>
                        {
                            switch (pair.Key)
                            {
                                case "fastestFee":
                                    return 1;
                                case "halfHourFee": return 3;
                                case "hourFee": return 6;
                                case "economyFee" when recommendedFees.TryGetValue("minimumFee", out var minFee) && minFee == pair.Value : return 144;
                                case "economyFee": return 72;
                                case "minimumFee": return 144;
                            }

                            return -1;
                        }, pair => new FeeRate(pair.Value));
                        return (Dictionary<int, FeeRate>)entry.Value;
                    });
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }

    public class NBXplorerFeeProviderFactory : IFeeProviderFactory
    {
        public NBXplorerFeeProviderFactory(ExplorerClientProvider explorerClients,IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
        {
            ArgumentNullException.ThrowIfNull(explorerClients);
            _ExplorerClients = explorerClients;
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
        }

        private readonly ExplorerClientProvider _ExplorerClients;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;

        public FeeRate Fallback { get; set; } = new FeeRate(100L, 1);

        ConcurrentDictionary<BTCPayNetworkBase, IFeeProvider> _Cache = new();


        public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
        {
            return _Cache.GetOrAdd(network, @base =>
            {
                if (network.IsBTC)
                {
                    return new MempoolSpaceFeeProvider(_httpClientFactory, _memoryCache,
                        network is BTCPayNetwork bnetwork &&
                        bnetwork.NBitcoinNetwork.ChainName == ChainName.Testnet);
                }

                return new NBXplorerFeeProvider(this, _ExplorerClients.GetExplorerClient(network));
            });
        }
    }

    public class NBXplorerFeeProvider : IFeeProvider
    {
        public NBXplorerFeeProvider(NBXplorerFeeProviderFactory parent, ExplorerClient explorerClient)
        {
            ArgumentNullException.ThrowIfNull(explorerClient);
            _Factory = parent;
            _ExplorerClient = explorerClient;
        }

        readonly NBXplorerFeeProviderFactory _Factory;
        readonly ExplorerClient _ExplorerClient;

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            try
            {
                return (await _ExplorerClient.GetFeeRateAsync(blockTarget).ConfigureAwait(false)).FeeRate;
            }
            catch (NBXplorerException ex) when (ex.Error.HttpCode == 400 &&
                                                ex.Error.Code == "fee-estimation-unavailable")
            {
                return _Factory.Fallback;
            }
        }
    }
}
