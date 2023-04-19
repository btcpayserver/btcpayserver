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
        public virtual async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/info",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningNodeInformationData>(response);
        }

        public virtual async Task<LightningNodeBalanceData> GetLightningNodeBalance(string cryptoCode,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/balance",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningNodeBalanceData>(response);
        }

        public virtual async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/connect", bodyPayload: request,
                    method: HttpMethod.Post), token);
            await HandleResponse(response);
        }

        public virtual async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/channels",
                    method: HttpMethod.Get), token);
            return await HandleResponse<IEnumerable<LightningChannelData>>(response);
        }

        public virtual async Task OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/channels", bodyPayload: request,
                    method: HttpMethod.Post), token);
            await HandleResponse(response);
        }

        public virtual async Task<string> GetLightningDepositAddress(string cryptoCode, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/address", method: HttpMethod.Post), token);
            return await HandleResponse<string>(response);
        }

        public virtual async Task<LightningPaymentData> PayLightningInvoice(string cryptoCode, PayLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/invoices/pay", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<LightningPaymentData>(response);
        }

        public virtual async Task<LightningPaymentData> GetLightningPayment(string cryptoCode,
            string paymentHash, CancellationToken token = default)
        {
            if (paymentHash == null)
                throw new ArgumentNullException(nameof(paymentHash));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/payments/{paymentHash}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningPaymentData>(response);
        }

        public virtual async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode,
            string invoiceId, CancellationToken token = default)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/invoices/{invoiceId}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningInvoiceData>(response);
        }

        public virtual async Task<LightningInvoiceData[]> GetLightningInvoices(string cryptoCode,
            bool? pendingOnly = null, long? offsetIndex = null, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            if (pendingOnly is bool v)
            {
                queryPayload.Add("pendingOnly", v.ToString());
            }
            if (offsetIndex is > 0)
            {
                queryPayload.Add("offsetIndex", offsetIndex);
            }

            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/invoices", queryPayload), token);
            return await HandleResponse<LightningInvoiceData[]>(response);
        }

        public virtual async Task<LightningPaymentData[]> GetLightningPayments(string cryptoCode,
            bool? includePending = null, long? offsetIndex = null, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            if (includePending is bool v)
            {
                queryPayload.Add("includePending", v.ToString());
            }
            if (offsetIndex is > 0)
            {
                queryPayload.Add("offsetIndex", offsetIndex);
            }

            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/server/lightning/{cryptoCode}/payments", queryPayload), token);
            return await HandleResponse<LightningPaymentData[]>(response);
        }

        public virtual async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode, CreateLightningInvoiceRequest request,
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
