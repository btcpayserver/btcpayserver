using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Hosting
{
    public class LoadCurrencyNameTableStartupTask : IStartupTask
    {
        private readonly CurrencyNameTable _currencyNameTable;

        public LoadCurrencyNameTableStartupTask(CurrencyNameTable currencyNameTable)
        {
            _currencyNameTable = currencyNameTable;
        }
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await _currencyNameTable.ReloadCurrencyData(cancellationToken);
        }
    }
}
