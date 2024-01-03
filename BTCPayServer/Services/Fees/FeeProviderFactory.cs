using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Fees;

public static class FeeProviderExtensions
{
    public static IServiceCollection AddFeeProviders(this IServiceCollection services, IServiceProvider bootstrapServiceProvider)
    {
        var networkProvider = bootstrapServiceProvider.GetRequiredService<NBXplorerNetworkProvider>();

        foreach (var network in networkProvider.GetAll())
        {
            
            if (network.CryptoCode == "BTC" && network.NBitcoinNetwork.ChainName != ChainName.Regtest)
            {

                services.AddKeyedSingleton<IFeeProvider>(network.CryptoCode, (provider, o) => new MempoolSpaceFeeProvider(
                    provider.GetRequiredService<IMemoryCache>(),
                    $"MempoolSpaceFeeProvider-{network.CryptoCode}",
                    provider.GetRequiredService<IHttpClientFactory>(),
                    network is { } n &&
                    n.NBitcoinNetwork.ChainName == ChainName.Testnet));
            }

            services.AddKeyedSingleton<IFeeProvider>(network.CryptoCode, (provider, o) => new NBXplorerFeeProvider(
                provider.GetRequiredService<ExplorerClientProvider>().GetExplorerClient(network.CryptoCode)));

            services.AddKeyedSingleton<IFeeProvider>(network.CryptoCode,
                (provider, o) => new StaticFeeProvider(new FeeRate(100L, 1)));
        }
        services.AddSingleton<IFeeProviderFactory, FeeProviderFactory>();
        return services;
    }
    
}

public class FeeProviderFactory : IFeeProviderFactory
{
    private readonly IEnumerable<IFeeProvider> _feeProviders;
    private readonly IServiceProvider _serviceProvider;

    public FeeProviderFactory(
        ExplorerClientProvider explorerClients,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        IEnumerable<IFeeProvider> feeProviders,
        IServiceProvider serviceProvider)
    {
        _feeProviders = feeProviders;
        _serviceProvider = serviceProvider;
    }

    private readonly ConcurrentDictionary<BTCPayNetworkBase, FallbackFeeProvider> _cached = new();
    public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
    {
        return _cached.GetOrAdd(network, n =>
        {
            var feeProviders = _serviceProvider.GetKeyedServices<IFeeProvider>(network.CryptoCode).Concat(_feeProviders).ToArray();
            if (!feeProviders.Any())
                throw new NotSupportedException($"No fee provider for this network ({network.CryptoCode})");

            return new FallbackFeeProvider(feeProviders.ToArray());
        });
    }
}
