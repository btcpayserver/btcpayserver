using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using NBitcoin;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<InvoiceData>> GetInvoices(string storeId, string[] orderId = null,
            InvoiceStatus[] status = null,
            DateTimeOffset? startDate = null,
            DateTimeOffset? endDate = null,
            string textSearch = null,
            bool includeArchived = false,
            int? skip = null,
            int? take = null,
            CancellationToken token = default)
        {
            Dictionary<string, object> queryPayload = new Dictionary<string, object>();
            queryPayload.Add(nameof(includeArchived), includeArchived);

            if (startDate is DateTimeOffset s)
                queryPayload.Add(nameof(startDate), Utils.DateTimeToUnixTime(s));

            if (endDate is DateTimeOffset e)
                queryPayload.Add(nameof(endDate), Utils.DateTimeToUnixTime(e));

            if (orderId != null)
                queryPayload.Add(nameof(orderId), orderId);
            if (textSearch != null)
                queryPayload.Add(nameof(textSearch), textSearch);
            if (status != null)
                queryPayload.Add(nameof(status), status.Select(s => s.ToString().ToLower()).ToArray());

            if (skip != null)
            {
                queryPayload.Add(nameof(skip), skip);
            }

            if (take != null)
            {
                queryPayload.Add(nameof(take), take);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/invoices",
                        queryPayload), token);
            return await HandleResponse<IEnumerable<InvoiceData>>(response);
        }

        public virtual async Task<InvoiceData> GetInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}"), token);
            return await HandleResponse<InvoiceData>(response);
        }
        public virtual async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods"), token);
            return await HandleResponse<InvoicePaymentMethodDataModel[]>(response);
        }

        public virtual async Task ArchiveInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}",
                    method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<InvoiceData> CreateInvoice(string storeId,
            CreateInvoiceRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<InvoiceData>(response);
        }

        public virtual async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId,
            UpdateInvoiceRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}", bodyPayload: request,
                    method: HttpMethod.Put), token);
            return await HandleResponse<InvoiceData>(response);
        }

        public virtual async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.Status != InvoiceStatus.Settled && request.Status != InvoiceStatus.Invalid)
                throw new ArgumentOutOfRangeException(nameof(request.Status), "Status can only be Invalid or Complete");
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/status", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<InvoiceData>(response);
        }

        public virtual async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/unarchive",
                    method: HttpMethod.Post), token);
            return await HandleResponse<InvoiceData>(response);
        }

        public virtual async Task ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods/{paymentMethod}/activate",
                    method: HttpMethod.Post), token);
            await HandleResponse(response);
        }

        public virtual async Task<PullPaymentData> RefundInvoice(
            string storeId,
            string invoiceId,
            RefundInvoiceRequest request,
            CancellationToken token = default
        )
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/refund", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<PullPaymentData>(response);
        }
    }
}
