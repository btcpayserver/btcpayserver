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
}
