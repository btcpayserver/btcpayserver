using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class FeeProviderFactory : IFeeProviderFactory
{
    public FeeProviderFactory(ExplorerClientProvider explorerClients,IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
    {
        ArgumentNullException.ThrowIfNull(explorerClients);
        _explorerClients = explorerClients;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _staticFeeProvider = new StaticFeeProvider(new FeeRate(100L, 1));
    }

    private readonly ExplorerClientProvider _explorerClients;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;

    private readonly ConcurrentDictionary<BTCPayNetworkBase, IFeeProvider> _cache = new();
    private readonly StaticFeeProvider _staticFeeProvider;


    public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
    {
        return _cache.GetOrAdd(network, @base =>
        {
            var nbxfeeProvider = new NBXplorerFeeProvider(_staticFeeProvider, _explorerClients.GetExplorerClient(@base));
            if (network.IsBTC)
            {
                return new MempoolSpaceFeeProvider(_httpClientFactory, _memoryCache, nbxfeeProvider,
                    network is BTCPayNetwork bnetwork &&
                    bnetwork.NBitcoinNetwork.ChainName == ChainName.Testnet);
            }
            return nbxfeeProvider;
        });
    }
}