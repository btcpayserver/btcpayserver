using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/info",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningNodeInformationData>(response);
        }

        public async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/connect", bodyPayload: request,
                    method: HttpMethod.Post), token);
            HandleResponse(response);
        }

        public async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/channels",
                    method: HttpMethod.Get), token);
            return await HandleResponse<IEnumerable<LightningChannelData>>(response);
        }

        public async Task<string> OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/channels", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<string>(response);
        }

        public async Task<string> GetLightningDepositAddress(string cryptoCode, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/address", method: HttpMethod.Post), token);
            return await HandleResponse<string>(response);
        }


        public async Task PayLightningInvoice(string cryptoCode, PayLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/pay", bodyPayload: request,
                    method: HttpMethod.Post), token);
            HandleResponse(response);
        }

        public async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode,
            string invoiceId, CancellationToken token = default)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/invoices/{invoiceId}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningInvoiceData>(response);
        }

        public async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode, CreateLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/invoices", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<LightningInvoiceData>(response);
        }
    }
}
