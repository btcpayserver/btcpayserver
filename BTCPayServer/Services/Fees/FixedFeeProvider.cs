using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Fees
{
    public class FixedFeeProvider : IFeeProvider, IFeeProviderFactory
    {
        public FixedFeeProvider(FeeRate feeRate)
        {
            FeeRate = feeRate;
        }

        public FeeRate FeeRate
        {
            get; set;
        }

        public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
        {
            return new FixedFeeProvider(FeeRate);
        }

        public Task<FeeRate> GetFeeRateAsync(int blockTarget)
        {
            return Task.FromResult(FeeRate);
        }
    }
}
