using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public abstract class BaseFeeProvider : IFeeProvider
{
    private readonly IFeeProvider _fallback;

    public BaseFeeProvider(IFeeProvider fallback)
    {
        _fallback = fallback;
    }

    public abstract Task<FeeRate> GetFeeRate(int blockTarget = 20);
    public Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
    {
        try
        {
            return GetFeeRate(blockTarget);
        }
        catch
        {
            return _fallback.GetFeeRateAsync(blockTarget);
        }
    }
        
        
}