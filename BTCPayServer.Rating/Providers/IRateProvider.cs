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
        /// <summary>
        /// Returns rates of the provider
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException">If using this provider isn't supported (For example if a <see cref="IContextualRateProvider"/> requires a context)</exception>
        Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken);
    }

    public interface IRateContext { }
    public interface IHasStoreIdRateContext : IRateContext
    {
        string StoreId { get; }
    }
    public record StoreIdRateContext(string StoreId) : IHasStoreIdRateContext;

    /// <summary>
    /// A rate provider which know additional context about the rate query.
    /// </summary>
    public interface IContextualRateProvider : IRateProvider
    {
        /// <summary>
        /// Returns rates of the provider when a context is available
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException">If using this provider isn't getting an expected context</exception>
        Task<PairRate[]> GetRatesAsync(IRateContext context, CancellationToken cancellationToken);
    }
}
