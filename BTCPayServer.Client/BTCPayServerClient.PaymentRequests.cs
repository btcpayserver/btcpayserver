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
        public virtual async Task<IEnumerable<PaymentRequestData>> GetPaymentRequests(string storeId,
            bool includeArchived = false,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests",
                        new Dictionary<string, object>() { { nameof(includeArchived), includeArchived } }), token);
            return await HandleResponse<IEnumerable<PaymentRequestData>>(response);
        }

        public virtual async Task<PaymentRequestData> GetPaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}"), token);
            return await HandleResponse<PaymentRequestData>(response);
        }

        public virtual async Task ArchivePaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}",
                    method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<Client.Models.InvoiceData> PayPaymentRequest(string storeId, string paymentRequestId, PayPaymentRequestRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (storeId is null)
                throw new ArgumentNullException(nameof(storeId));
            if (paymentRequestId is null)
                throw new ArgumentNullException(nameof(paymentRequestId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}/pay", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<Client.Models.InvoiceData>(response);
        }

        public virtual async Task<PaymentRequestData> CreatePaymentRequest(string storeId,
            CreatePaymentRequestRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<PaymentRequestData>(response);
        }

        public virtual async Task<PaymentRequestData> UpdatePaymentRequest(string storeId, string paymentRequestId,
            UpdatePaymentRequestRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}", bodyPayload: request,
                    method: HttpMethod.Put), token);
            return await HandleResponse<PaymentRequestData>(response);
        }
    }
}
