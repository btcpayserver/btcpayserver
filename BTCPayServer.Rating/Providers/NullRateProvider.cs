﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class NullRateProvider : IRateProvider
    {
        private NullRateProvider()
        {

        }
        private static readonly NullRateProvider _Instance = new NullRateProvider();
        public static NullRateProvider Instance
        {
            get
            {
                return _Instance;
            }
        }
        public Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExchangeRates());
        }
    }
}
