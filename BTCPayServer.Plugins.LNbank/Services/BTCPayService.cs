using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class BTCPayService
    {
        public const string CryptoCode = "BTC";
        private readonly IBTCPayServerClientFactory _clientFactory;

        public BTCPayService(IBTCPayServerClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<LightningInvoiceData> CreateLightningInvoice(LightningInvoiceCreateRequest req)
        {
            var client = await Client();
            return await client.CreateLightningInvoice(CryptoCode, new CreateLightningInvoiceRequest
            {
                Amount = req.Amount,
                Description = req.Description,
                Expiry = req.Expiry,
                PrivateRouteHints = req.PrivateRouteHints
            });
        }
        
        public async Task PayLightningInvoice(LightningInvoicePayRequest req)
        {
            var client = await Client();
            await client.PayLightningInvoice(CryptoCode, new PayLightningInvoiceRequest
            {
                BOLT11 = req.PaymentRequest
            });
        }

        public async Task<LightningInvoiceData> GetLightningInvoice(string invoiceId, CancellationToken cancellationToken = default)
        {
            var client = await Client();
            return await client.GetLightningInvoice(CryptoCode, invoiceId, cancellationToken);
        }

        public async Task<LightningNodeInformationData> GetLightningNodeInfo(CancellationToken cancellationToken = default)
        {
            var client = await Client();
            return await client.GetLightningNodeInfo(CryptoCode, cancellationToken);
        }

        public async Task<IEnumerable<LightningChannelData>> ListLightningChannels(CancellationToken cancellationToken = default)
        {
            var client = await Client();
            return await client.GetLightningNodeChannels(CryptoCode, cancellationToken);
        }

        public async Task<string> GetLightningDepositAddress(CancellationToken cancellationToken = default)
        {
            var client = await Client();
            return await client.GetLightningDepositAddress(CryptoCode, cancellationToken);
        }

        public async Task OpenLightningChannel(OpenLightningChannelRequest req, CancellationToken cancellationToken = default)
        {
            var client = await Client();
            await client.OpenLightningChannel(CryptoCode, req, cancellationToken);
        }

        public async Task ConnectToLightningNode(ConnectToNodeRequest req, CancellationToken cancellationToken = default)
        {
            var client = await Client();
            await client.ConnectToLightningNode(CryptoCode, req, cancellationToken);
        }

        private async Task<BTCPayServerClient> Client()
        {
            return await _clientFactory.Create(null, new string[0]);
        }
    }
}
