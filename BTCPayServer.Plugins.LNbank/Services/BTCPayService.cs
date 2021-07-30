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
        private BTCPayServerClient _client;
        private readonly IBTCPayServerClientFactory _clientFactory;
        private static readonly string CryptoCode = "BTC";
        
        public BTCPayService(IBTCPayServerClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<LightningInvoiceData> CreateLightningInvoice(LightningInvoiceCreateRequest req)
        {
            return await (await Client()).CreateLightningInvoice(CryptoCode, new CreateLightningInvoiceRequest
            {
                Amount = req.Amount,
                Description = req.Description,
                Expiry = req.Expiry
            });
        }
        public async Task PayLightningInvoice(LightningInvoicePayRequest req)
        {
            await (await Client()).PayLightningInvoice(CryptoCode, new PayLightningInvoiceRequest
            {
                BOLT11 = req.PaymentRequest
            });
        }

        public async Task<LightningInvoiceData> GetLightningInvoice(string invoiceId, CancellationToken cancellationToken = default)
        {
            return await (await Client()).GetLightningInvoice(CryptoCode, invoiceId, cancellationToken);
        }

        public async Task<LightningNodeInformationData> GetLightningNodeInfo(CancellationToken cancellationToken = default)
        {
            return await (await Client()).GetLightningNodeInfo(CryptoCode, cancellationToken);
        }

        public async Task<IEnumerable<LightningChannelData>> ListLightningChannels(CancellationToken cancellationToken = default)
        {
            return await (await Client()).GetLightningNodeChannels(CryptoCode, cancellationToken);
        }

        public async Task<string> GetLightningDepositAddress(CancellationToken cancellationToken = default)
        {
            
            return await (await Client()).GetLightningDepositAddress(CryptoCode, cancellationToken);
        }

        public async Task OpenLightningChannel(OpenLightningChannelRequest req, CancellationToken cancellationToken = default)
        {
            await (await Client()).OpenLightningChannel(CryptoCode, req, cancellationToken);
        }

        public async Task ConnectToLightningNode(ConnectToNodeRequest req, CancellationToken cancellationToken = default)
        {
            await (await Client()).ConnectToLightningNode(CryptoCode, req, cancellationToken);
        }

        private async Task<BTCPayServerClient> Client()
        {
            _client ??= await _clientFactory.Create(null, new string[0]);
            return _client;
        }
    }
}
