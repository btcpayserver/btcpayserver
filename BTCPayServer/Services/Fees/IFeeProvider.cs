using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public interface IFeeProvider
    {
        Task<FeeRate> GetFeeRateAsync();
    }
}
