using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Services
{
    public interface IFeeProvider
    {
        Task<FeeRate> GetFeeRateAsync(int blockTarget = 20);
    }
}
