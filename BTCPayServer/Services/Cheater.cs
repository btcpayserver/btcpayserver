using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.RPC;
namespace BTCPayServer.Services
{
    public class Cheater : IHostedService
    {
        private readonly ExplorerClientProvider _prov;
        private readonly InvoiceRepository _invoiceRepository;
        public RPCClient CashCow { get; set; }

        public Cheater(
            ExplorerClientProvider prov,
            InvoiceRepository invoiceRepository)
        {
            CashCow = prov.GetExplorerClient("BTC")?.RPCClient;
            _prov = prov;
            _invoiceRepository = invoiceRepository;
        }

        public RPCClient GetCashCow(string cryptoCode)
        {
            return _prov.GetExplorerClient(cryptoCode)?.RPCClient;
        }

        public async Task UpdateInvoiceExpiry(string invoiceId, TimeSpan seconds)
        {
            await _invoiceRepository.UpdateInvoiceExpiry(invoiceId, seconds);
        }

        async Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
			if (CashCow is { } c)
				await c.ScanRPCCapabilitiesAsync(cancellationToken);
		}

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
