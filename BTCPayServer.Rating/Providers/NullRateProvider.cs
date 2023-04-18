using System;
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

        public RateSourceInfo RateSourceInfo => new RateSourceInfo("NULL", "NULL", "https://NULL.NULL");

        public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Array.Empty<PairRate>());
        }
    }
}
