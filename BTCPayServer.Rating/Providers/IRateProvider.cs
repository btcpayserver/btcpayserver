using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public interface IRateProvider
    {
        Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken);
    }
}
