using NBitcoin;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public interface IFeeProvider
    {
        Task<FeeRate> GetFeeRateAsync(int blockTarget = 20);
    }
}
