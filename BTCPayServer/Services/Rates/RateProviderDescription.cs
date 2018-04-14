using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public interface RateProviderDescription
    {
        IRateProvider CreateRateProvider(IServiceProvider serviceProvider);
    }
}
