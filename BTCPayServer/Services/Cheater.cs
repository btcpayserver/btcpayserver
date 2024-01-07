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
#if ALTCOINS
            var liquid = _prov.GetNetwork("LBTC");
            if (liquid is not null)
            {
                var lbtcrpc = GetCashCow(liquid.CryptoCode);
                await lbtcrpc.SendCommandAsync("rescanblockchain");
                var elements = _prov.NetworkProviders.GetAll().OfType<Plugins.Altcoins.ElementsBTCPayNetwork>();
                foreach (Plugins.Altcoins.ElementsBTCPayNetwork element in elements)
                {
                    try
                    {
                        if (element.AssetId is null)
                        {
                            var issueAssetResult = await lbtcrpc.SendCommandAsync("issueasset", 100000, 0);
                            element.AssetId = uint256.Parse(issueAssetResult.Result["asset"].ToString());
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
#else
            if (CashCow is { } c)
                await c.ScanRPCCapabilitiesAsync(cancellationToken);
#endif
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
