using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class StaticFeeProvider : IFeeProvider
{
    private readonly FeeRate _feeRate;

    public StaticFeeProvider(FeeRate feeRate)
    {
        _feeRate = feeRate;
    }

    public Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
    {
        return Task.FromResult(_feeRate);
    }
}