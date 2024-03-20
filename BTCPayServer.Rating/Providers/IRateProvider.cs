#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public interface IRateProvider
    {
        RateSourceInfo RateSourceInfo { get; }
        Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken);
    }

    public interface IDynamicRateProvider
    {
        public RateSourceInfo RateSourceInfo { get; }

        Task<IRateProvider?> GetRateProvider(string context, CancellationToken cancellationToken);
    }
    
    public class DynamicRateProvider : IDynamicRateProvider
    {
        private readonly Func<string, CancellationToken, Task<IRateProvider>> _fetch;

        public DynamicRateProvider(RateSourceInfo rateSourceInfo,Func<string, CancellationToken, Task<IRateProvider>> fetch)
        {
            RateSourceInfo = rateSourceInfo;
            _fetch = fetch;
        }

        public RateSourceInfo RateSourceInfo { get; }

        public async Task<IRateProvider> GetRateProvider(string context, CancellationToken cancellationToken)
        {
            return await _fetch(context, cancellationToken);
        }
    }
}
