using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public interface IRateProvider
    {
        Task<ExchangeRates> GetRatesAsync();
    }
}
