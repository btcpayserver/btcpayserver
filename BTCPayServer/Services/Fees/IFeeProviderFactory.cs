using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public interface IFeeProviderFactory
    {
        IFeeProvider CreateFeeProvider(BTCPayNetwork network);
    }
}
