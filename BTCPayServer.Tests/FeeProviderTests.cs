using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Fees;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Tests;

public class FeeProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeeRefreshKeepsCachedEstimatesDuringAnExplorerOutageAndRecovers(bool simulateTimeout)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var explorerNetworks = new NBXplorerNetworkProvider(ChainName.Regtest);
        var network = new BTCPayNetwork
        {
            CryptoCode = "BTC",
            NBXplorerNetwork = explorerNetworks.GetFromCryptoCode("BTC")
        };
        var explorer = network.NBXplorerNetwork.CreateExplorerClient(new Uri("http://explorer.invalid/"));
        explorer.SetNoAuth();
        var requestCount = 0;
        decimal? feeRate = 0.0001m;
        using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
        {
            requestCount++;
            if (feeRate is not { } rate)
            {
                if (simulateTimeout)
                    throw new OperationCanceledException("Request timed out");
                throw new HttpRequestException("Connection refused");
            }

            var batch = JsonParser.ParseArray(request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            var response = new JArray(batch.Select(command => new JObject
            {
                ["id"] = command["id"],
                ["result"] = new JObject { ["feerate"] = rate, ["blocks"] = 1 },
                ["error"] = null
            }));
            return TestHttpMessageHandler.JsonResponse(response.ToString(Formatting.None));
        }));
        explorer.RPCClient.HttpClient = httpClient;

        var networks = new BTCPayNetworkProvider([network], explorerNetworks, new Logs());
        var environment = new BTCPayServerEnvironment(null, networks, null, new BTCPayServerOptions());
        var dashboard = new NBXplorerDashboard();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FeeProviderFactory(environment,
            new TestExplorerClientProvider(networks, dashboard, network, explorer), null, dashboard, cache,
            NullLogger<FeeProviderFactory>.Instance);
        var feeProvider = factory.CreateFeeProvider(network);

        await factory.Do(cancellationToken);
        Assert.Equal(10m, (await feeProvider.GetFeeRateAsync(1)).SatoshiPerByte);

        feeRate = null;
        await factory.Do(cancellationToken);
        Assert.Equal(10m, (await feeProvider.GetFeeRateAsync(1)).SatoshiPerByte);

        feeRate = 0.0002m;
        await factory.Do(cancellationToken);
        Assert.Equal(20m, (await feeProvider.GetFeeRateAsync(1)).SatoshiPerByte);
        Assert.Equal(3, requestCount);
    }

    private sealed class TestExplorerClientProvider(
        BTCPayNetworkProvider networks, NBXplorerDashboard dashboard, BTCPayNetwork network, ExplorerClient explorer)
        : ExplorerClientProvider(networks, dashboard)
    {
        public override IEnumerable<(BTCPayNetwork, ExplorerClient)> GetAll() => [(network, explorer)];
    }
}
