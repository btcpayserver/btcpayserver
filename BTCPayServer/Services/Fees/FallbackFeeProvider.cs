#nullable enable
using System;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Services.Fees
{
    public class FallbackFeeProvider : IFeeProvider
    {
        public FallbackFeeProvider(IFeeProvider[] providers)
        {
            Providers = providers;
        }

        public IFeeProvider[] Providers { get; }

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            for (int i = 0; i < Providers.Length; i++)
            {
                try
                {
                    return await Providers[i].GetFeeRateAsync(blockTarget);
                }
                catch when (i < Providers.Length - 1)
                {
                }
            }
            throw new NotSupportedException("No provider available");
        }
    }
}
