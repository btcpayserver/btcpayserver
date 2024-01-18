#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class MempoolSpaceFeeProvider(
    IMemoryCache MemoryCache,
    string CacheKey,
    IHttpClientFactory HttpClientFactory,
    bool Testnet) : IFeeProvider
{
    private string ExplorerLink = Testnet switch
    {
        true => "https://mempool.space/testnet/api/v1/fees/recommended",
        false => "https://mempool.space/api/v1/fees/recommended"
    };

    public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
    {
        var result = await GetFeeRatesAsync();

        return result.TryGetValue(blockTarget, out var feeRate)
            ? feeRate
            :
            //try get the closest one
            result[result.Keys.MinBy(key => Math.Abs(key - blockTarget))];
    }

    public async Task<Dictionary<int, FeeRate>> GetFeeRatesAsync()
    {
        try
        {
            return  (await  MemoryCache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await GetFeeRatesCore();
            }))!;
        }
        catch (Exception e)
        {
            MemoryCache.Remove(CacheKey);
            throw;
        }
    }

    protected virtual async Task<Dictionary<int, FeeRate>> GetFeeRatesCore()
    {
        var client = HttpClientFactory.CreateClient(nameof(MempoolSpaceFeeProvider));
        using var result = await client.GetAsync(ExplorerLink, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        result.EnsureSuccessStatusCode();
        var recommendedFees = await result.Content.ReadAsAsync<Dictionary<string, decimal>>();
        var feesByBlockTarget = new Dictionary<int, FeeRate>();
        foreach ((var feeId, decimal value) in recommendedFees)
        {
            var target = feeId switch
            {
                "fastestFee" => 1,
                "halfHourFee" => 3,
                "hourFee" => 6,
                "economyFee" when recommendedFees.TryGetValue("minimumFee", out var minFee) && minFee == value => 144,
                "economyFee" => 72,
                "minimumFee" => 144,
                _ => -1
            };
            feesByBlockTarget.TryAdd(target, new FeeRate(RandomizeByPercentage(value, 10)));
        }
        return feesByBlockTarget;
    }
    
    static decimal RandomizeByPercentage(decimal value, int percentage)
    {
        decimal range = value * percentage / 100m;
        var res = value + range * (Random.Shared.NextDouble() < 0.5 ? -1 : 1);

        return res switch
        {
            < 1m => 1m,
            > 850m => 2000m,
            _ => res
        };
    }
}
