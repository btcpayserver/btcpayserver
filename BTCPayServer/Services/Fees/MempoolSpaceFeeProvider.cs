#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class MempoolSpaceFeeProvider(
    IMemoryCache memoryCache,
    string cacheKey,
    IHttpClientFactory httpClientFactory,
    bool testnet) : IFeeProvider
{
    private readonly string _explorerLink = testnet switch
    {
        true => "https://mempool.space/testnet/api/v1/fees/recommended",
        false => "https://mempool.space/api/v1/fees/recommended"
    };

    public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
    {
        var result = await GetFeeRatesAsync();

        return InterpolateOrBound(result, blockTarget);

    }

    internal static FeeRate InterpolateOrBound(BlockFeeRate[] ordered, int target)
    {
        var (lb, hb) = (ordered[0], ordered[^1]);
        target = Math.Clamp(target, lb.Blocks, hb.Blocks);
        foreach (var t in ordered)
        {
            if (t.Blocks > lb.Blocks && t.Blocks <= target)
                lb = t;
            if (t.Blocks < hb.Blocks && t.Blocks >= target)
                hb = t;
        }
        if (hb.Blocks == lb.Blocks)
            return hb.FeeRate;
        var a = (decimal)(target - lb.Blocks) / (hb.Blocks - lb.Blocks);
        return new FeeRate((1 - a) * lb.FeeRate.SatoshiPerByte + a * hb.FeeRate.SatoshiPerByte);
    }

    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(25);
    public async Task RefreshCache()
    {
        var rate = await GetFeeRatesCore();
        memoryCache.Set(cacheKey, rate, _expiration);
    }

    public bool CachedOnly { get; set; }
    internal async Task<BlockFeeRate[]> GetFeeRatesAsync()
    {
        if (CachedOnly)
            return memoryCache.Get(cacheKey) as BlockFeeRate[] ?? throw new InvalidOperationException("Fee rates unavailable");
        try
        {
            return  (await  memoryCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _expiration;
                return await GetFeeRatesCore();
            }))!;
        }
        catch (Exception)
        {
            memoryCache.Remove(cacheKey);
            throw;
        }
    }
    internal record BlockFeeRate(int Blocks, FeeRate FeeRate);
    async Task<BlockFeeRate[]> GetFeeRatesCore()
    {
        var client = httpClientFactory.CreateClient(nameof(MempoolSpaceFeeProvider));
        using var result = await client.GetAsync(_explorerLink);
        result.EnsureSuccessStatusCode();
        var recommendedFees = await result.Content.ReadAsAsync<Dictionary<string, decimal>>();
        var r = new List<BlockFeeRate>();

        foreach (var (feeId, value) in recommendedFees)
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
            r.Add(new(target, new FeeRate(value)));
        }

        var ordered = r.OrderBy(k => k.Blocks).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            // Randomize a bit
            ordered[i] = ordered[i] with { FeeRate = new FeeRate(RandomizeByPercentage(ordered[i].FeeRate.SatoshiPerByte, 10m)) };
            if (i > 0) // Make sure feerate always decrease
                ordered[i] = ordered[i] with { FeeRate = FeeRate.Min(ordered[i - 1].FeeRate, ordered[i].FeeRate) };
        }
        return ordered;
    }

    internal static decimal RandomizeByPercentage(decimal value, decimal percentage)
    {
        if (value == 1.0m)
            return 1.0m;
        var range = (value * percentage) / 100m;
        return value + (range * 2.0m) * ((decimal)(Random.Shared.NextDouble() - 0.5));
    }
}
