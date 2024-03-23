using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class FeeProviderFactory : IFeeProviderFactory, IPeriodicTask
{
    public FeeProviderFactory(
    BTCPayServerEnvironment Environment,
    ExplorerClientProvider ExplorerClients,
    IHttpClientFactory HttpClientFactory,
    IMemoryCache MemoryCache)
    {
        _FeeProviders = new();

        // TODO: Pluginify this
        foreach ((var network, var client) in ExplorerClients.GetAll())
        {
            List<IFeeProvider> providers = new List<IFeeProvider>();
            if (network.IsBTC && Environment.NetworkType != ChainName.Regtest)
            {
                providers.Add(new MempoolSpaceFeeProvider(
                    MemoryCache,
                    $"MempoolSpaceFeeProvider-{network.CryptoCode}",
                    HttpClientFactory,
                    network is BTCPayNetwork n &&
                    n.NBitcoinNetwork.ChainName == ChainName.Testnet)
                {
                    CachedOnly = true
                });
            }
            providers.Add(new NBXplorerFeeProvider(client));
            providers.Add(new StaticFeeProvider(new FeeRate(100L, 1)));
            var fallback = new FallbackFeeProvider(providers.ToArray());
            _FeeProviders.Add(network, fallback);
        }
    }
    private readonly Dictionary<BTCPayNetworkBase, IFeeProvider> _FeeProviders;
    public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
    {
        return _FeeProviders.TryGetValue(network, out var prov) ? prov : throw new NotSupportedException($"No fee provider for this network ({network.CryptoCode})");
    }

    public async Task Do(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshCache(_FeeProviders.Values);
        }
        // Do not spam logs if mempoolspace is down
        catch (TaskCanceledException)
        {
        }
        catch (HttpRequestException)
        {
        }
    }
    private Task RefreshCache(IEnumerable<IFeeProvider> feeProviders) => Task.WhenAll(feeProviders.Select(fp => RefreshCache(fp)));
    private Task RefreshCache(IFeeProvider fp) =>
        fp switch
        {
            FallbackFeeProvider ffp => Task.WhenAll(ffp.Providers.Select(p => RefreshCache(p))),
            MempoolSpaceFeeProvider mempool => mempool.RefreshCache(),
            _ => Task.CompletedTask
        };
}
