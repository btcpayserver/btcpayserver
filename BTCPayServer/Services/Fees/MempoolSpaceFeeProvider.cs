using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class MempoolSpaceFeeProvider : BaseFeeProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly bool _testnet;
        
    public MempoolSpaceFeeProvider(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache,
        IFeeProvider fallback,
        bool testnet = false) : base(fallback)
    {
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _testnet = testnet;
    }

    public override async Task<FeeRate> GetFeeRate(int blockTarget = 20)
    {
        var result = await GetFeeRatesAsync();

        return result.TryGetValue(blockTarget, out var feeRate)
            ? feeRate
            :
            //try get the closest one
            result[result.Keys.MinBy(key => Math.Abs(key - blockTarget))];
    }

    private SemaphoreSlim _semaphoreSlim = new(1, 1);

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