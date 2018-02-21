﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public interface IRateProviderFactory
    {
        IRateProvider GetRateProvider(BTCPayNetwork network, bool longCache);
    }
}
