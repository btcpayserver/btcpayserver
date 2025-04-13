#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using Org.BouncyCastle.Asn1.X509;
using YamlDotNet.Core.Tokens;

namespace BTCPayServer.Services.Fees;

public class MempoolSpaceFeeProvider(
    IMemoryCache memoryCache,
    string cacheKey,
    IHttpClientFactory httpClientFactory,
    bool testnet) : IFeeProvider
{
    private string ExplorerLink = testnet switch
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
        (BlockFeeRate lb, BlockFeeRate hb) = (ordered[0], ordered[^1]);
        target = Math.Clamp(target, lb.Blocks, hb.Blocks);
        for (int i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].Blocks > lb.Blocks && ordered[i].Blocks <= target)
                lb = ordered[i];
            if (ordered[i].Blocks < hb.Blocks && ordered[i].Blocks >= target)
                hb = ordered[i];
        }
        if (hb.Blocks == lb.Blocks)
            return hb.FeeRate;
        var a = (decimal)(target - lb.Blocks) / (decimal)(hb.Blocks - lb.Blocks);
        return new FeeRate((1 - a) * lb.FeeRate.SatoshiPerByte + a * hb.FeeRate.SatoshiPerByte);
    }
    readonly TimeSpan Expiration = TimeSpan.FromMinutes(25);
    public async Task RefreshCache()
    {
        var rate = await GetFeeRatesCore();
        memoryCache.Set(cacheKey, rate, Expiration);
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
                entry.AbsoluteExpirationRelativeToNow = Expiration;
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
        using var result = await client.GetAsync(ExplorerLink);
        result.EnsureSuccessStatusCode();
        var recommendedFees = await result.Content.ReadAsAsync<Dictionary<string, decimal>>();
        var r = new List<BlockFeeRate>();
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
        if (value is 1)
            return 1;
        decimal range = (value * percentage) / 100m;
        var res = value + (range * 2.0m) * ((decimal)(Random.Shared.NextDouble() - 0.5));
        return res switch
        {
            < 1m => 1m,
            > 2000m => 2000m,
            _ => res
        };
    }
}
