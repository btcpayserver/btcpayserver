#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProvider(
        ExplorerClient explorerClient,
        IMemoryCache memoryCache,
        string cacheKey) : IFeeProvider
    {
        private readonly TimeSpan _expiration = TimeSpan.FromMinutes(25);
        private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            if (memoryCache.TryGetValue(GetCacheKey(blockTarget), out FeeRate? cachedFeeRate) && cachedFeeRate is not null)
                return cachedFeeRate;

            using var cts = new CancellationTokenSource(_requestTimeout);
            var feeRate = await FetchFeeRate(blockTarget, cts.Token);
            CacheFeeRate(blockTarget, feeRate);
            return feeRate;
        }

        public async Task RefreshCache(IEnumerable<int> blockTargets, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_requestTimeout);
            var refreshCancellationToken = cts.Token;
            var batch = explorerClient.RPCClient.PrepareBatch();
            var estimates = blockTargets
                .Distinct()
                .Select(blockTarget =>
                    (BlockTarget: blockTarget,
                        Estimate: batch.TryEstimateSmartFeeAsync(blockTarget, cancellationToken: refreshCancellationToken)))
                .ToArray();

            await batch.SendBatchAsync(refreshCancellationToken);

            foreach (var estimate in estimates)
            {
                // Bitcoin Core reports insufficient data in the result's errors array
                // without a fee rate, which TryEstimateSmartFeeAsync maps to null.
                if (await estimate.Estimate is { FeeRate: { } feeRate })
                    CacheFeeRate(estimate.BlockTarget, feeRate);
            }
        }

        private async Task<FeeRate> FetchFeeRate(int blockTarget, CancellationToken cancellationToken) =>
            (await explorerClient.RPCClient.TryEstimateSmartFeeAsync(blockTarget, cancellationToken: cancellationToken))?.FeeRate ??
            throw new InvalidOperationException("Fee estimation unavailable");

        private void CacheFeeRate(int blockTarget, FeeRate feeRate) =>
            memoryCache.Set(GetCacheKey(blockTarget), feeRate, _expiration);

        private string GetCacheKey(int blockTarget) => $"{cacheKey}-{blockTarget}";
    }
}
