#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Rating
{
    public static class Extensions
    {
        public static Task<PairRate[]> GetRatesAsyncWithMaybeContext(this IRateProvider rateProvider, IRateContext? context, CancellationToken cancellationToken)
        {
            if (rateProvider is IContextualRateProvider contextualRateProvider && context is { })
            {
                return contextualRateProvider.GetRatesAsync(context, cancellationToken);
            }
            else
            {
                return rateProvider.GetRatesAsync(cancellationToken);
            }
        }
        internal static async Task<T> ReadRateResponseAsAsync<T>(this HttpContent content, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
            return await content.ReadAsAsync<T>(timeout.Token);
        }
        public static decimal RoundToSignificant(this decimal value, int divisibility)
        {
            return RoundToSignificant(value, ref divisibility);
        }
        public static decimal RoundToSignificant(this decimal value, ref int divisibility)
        {
            if (value != 0m)
            {
                while (true)
                {
                    var rounded = decimal.Round(value, divisibility, MidpointRounding.AwayFromZero);
                    if ((Math.Abs(rounded - value) / value) < 0.01m)
                    {
                        value = rounded;
                        break;
                    }
                    divisibility++;
                }
            }
            return value;
        }
    }
}
