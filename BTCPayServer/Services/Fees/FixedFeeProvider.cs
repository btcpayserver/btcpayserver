using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Fees
{
    public class FixedFeeProvider : IFeeProvider
    {
        public FixedFeeProvider(FeeRate feeRate)
        {
            FeeRate = feeRate;
        }

        public FeeRate FeeRate
        {
            get; set;
        }

        public Task<FeeRate> GetFeeRateAsync()
        {
            return Task.FromResult(FeeRate);
        }
    }
}
