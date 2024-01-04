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

        return result.TryGetValue(blockTarget, out var feeRate)
            ? feeRate
            : InterpolateOrBound(result, blockTarget);
            
    }
    
    static FeeRate InterpolateOrBound(Dictionary<int, FeeRate> dictionary, int target)
    {
        // Find the keys closest to the target for interpolation
        int? k1 = null;
        int? k2 = null;

        foreach (int k in dictionary.Keys.Order())
        {
            k1 = k1 is null ? k : k2;
            k2 = k;
            if(target < k)
            {
                break;
            }
        }

        if (k1 is null)
        {
           throw new InvalidOperationException("No fee rate available");
        }
        
        var v1 = dictionary[k1!.Value].SatoshiPerByte;
        var v2 = dictionary[k2!.Value].SatoshiPerByte;

        // Linear interpolation formula
        return new FeeRate((decimal) (v1 + (v2 - v1) / (k1 - k2) * (target - k1))!);
    }

    public async Task<Dictionary<int, FeeRate>> GetFeeRatesAsync()
    {
        try
        {
            return  (await  memoryCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await GetFeeRatesCore();
            }))!;
        }
        catch (Exception e)
        {
            memoryCache.Remove(cacheKey);
            throw;
        }
    }

    protected virtual async Task<Dictionary<int, FeeRate>> GetFeeRatesCore()
    {
        var client = httpClientFactory.CreateClient(nameof(MempoolSpaceFeeProvider));
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
            feesByBlockTarget.TryAdd(target, new FeeRate(value));
        }
        // order feesByBlockTarget and then randomize them by 10%, but never allow the numbers to go below the previous one or higher than the next
        var ordered = feesByBlockTarget.OrderByDescending(kv => kv.Key).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            (int key, FeeRate value) = ordered[i];
            if (i == 0)
            {
                feesByBlockTarget[key] = new FeeRate(RandomizeByPercentage(value.SatoshiPerByte, 10));
            }
            else
            {
                var previous = feesByBlockTarget[ordered[i - 1].Key];
                var newValue = RandomizeByPercentage(value.SatoshiPerByte, 10);
                if (newValue > previous.SatoshiPerByte)
                {
                    newValue = previous.SatoshiPerByte;
                }
                feesByBlockTarget[key] = new FeeRate(newValue);
            }
        }
        
        return feesByBlockTarget;
    }
    
    static decimal RandomizeByPercentage(decimal value, int percentage)
    {
        if (value is 1)
        {
            return 1;
        }
        decimal range = value * percentage / 100m;
        var res = value + range * (Random.Shared.NextDouble() < 0.5 ? -1 : 1);

        return res switch
        {
            < 1m => 1m,
            > 2000m => 2000m,
            _ => res
        };
    }
}
