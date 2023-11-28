#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NBitcoin;

namespace BTCPayServer.Services.Fees
{
    public class FallbackFeeProvider(IFeeProvider[] Providers) : IFeeProvider
    {
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
