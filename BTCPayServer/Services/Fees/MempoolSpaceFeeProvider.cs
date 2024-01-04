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
            : InterpolateOrBound(result, blockTarget);
            
    }
    
    static FeeRate InterpolateOrBound(Dictionary<int, FeeRate> dictionary, int target)
    {
        // Get the smallest and largest keys
        int smallestKey = dictionary.Keys.Min();
        int largestKey = dictionary.Keys.Max();

        // If the target is outside the range, clamp to the nearest bound
        if (target <= smallestKey)
        {
            return dictionary[smallestKey];
        }
        if (target >= largestKey)
        {
            return dictionary[largestKey];
        }

        // Find the keys closest to the target for interpolation
        int key1 = 0, key2 = 0;
        decimal value1 = 0, value2 = 0;

        foreach (var key in dictionary.Keys)
        {
            if (key < target && (key1 == 0 || key > key1))
            {
                key1 = key;
                value1 = dictionary[key].SatoshiPerByte;
            }
            if (key > target && (key2 == 0 || key < key2))
            {
                key2 = key;
                value2 = dictionary[key].SatoshiPerByte;
            }
        }

        // Linear interpolation formula
        return new FeeRate(value1 + ((value2 - value1) / (key2 - key1)) * (target - key1));
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
                var previous = ordered[i - 1].Value;
                var newValue = RandomizeByPercentage(value.SatoshiPerByte, 10);
                if (newValue < previous.SatoshiPerByte)
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
            > 850m => 2000m,
            _ => res
        };
    }
}
