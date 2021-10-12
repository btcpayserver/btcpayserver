using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Services
{
    public class Cheater : IHostedService
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;

        public Cheater(BTCPayServerOptions opts, ApplicationDbContextFactory applicationDbContextFactory)
        {
            CashCow = new RPCClient(RPCCredentialString.Parse("server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3"), Bitcoin.Instance.GetNetwork(opts.NetworkType));
            _applicationDbContextFactory = applicationDbContextFactory;
        }
        public RPCClient CashCow
        {
            get;
            set;
        }

        public async Task UpdateInvoiceExpiry(string invoiceId, DateTimeOffset dateTimeOffset)
        {
            using (var ctx = _applicationDbContextFactory.CreateContext())
            {
                var invoiceData = await ctx.Invoices.FindAsync(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                // TODO change the expiry time. But how?
                await ctx.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            _ = CashCow.ScanRPCCapabilitiesAsync();
            return Task.CompletedTask;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
