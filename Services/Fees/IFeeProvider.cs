using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Services
{
    public interface IFeeProvider
    {
        Task<FeeRate> GetFeeRateAsync(int blockTarget = 20);
    }
}
