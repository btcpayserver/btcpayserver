using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<PaymentRequestBaseData>> GetPaymentRequests(string storeId,
        bool includeArchived = false,
        CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<PaymentRequestBaseData>>($"api/v1/stores/{storeId}/payment-requests",
            new Dictionary<string, object> { { nameof(includeArchived), includeArchived } }, HttpMethod.Get, token);
    }

    public virtual async Task<PaymentRequestBaseData> GetPaymentRequest(string? storeId, string paymentRequestId,
        CancellationToken token = default)
    {
        var path = storeId is null ? $"api/v1/payment-requests/{paymentRequestId}" : $"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}";
        return await SendHttpRequest<PaymentRequestBaseData>(path, null, HttpMethod.Get, token);
    }

    public virtual async Task ArchivePaymentRequest(string? storeId, string paymentRequestId,
        CancellationToken token = default)
    {
        var path = storeId is null ? $"api/v1/payment-requests/{paymentRequestId}" : $"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}";
        await SendHttpRequest(path, null, HttpMethod.Delete, token);
    }

    public virtual async Task<Client.Models.InvoiceData> PayPaymentRequest(string? storeId, string paymentRequestId, PayPaymentRequestRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (paymentRequestId is null) throw new ArgumentNullException(nameof(paymentRequestId));
        var path = storeId is null ? $"api/v1/payment-requests/{paymentRequestId}/pay" : $"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}/pay";
        return await SendHttpRequest<InvoiceData>(path, request, HttpMethod.Post, token);
    }

    public virtual async Task<PaymentRequestBaseData> CreatePaymentRequest(string storeId,
        PaymentRequestBaseData request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<PaymentRequestBaseData>($"api/v1/stores/{storeId}/payment-requests", request, HttpMethod.Post, token);
    }

    public virtual async Task<PaymentRequestBaseData> UpdatePaymentRequest(string? storeId, string paymentRequestId,
        PaymentRequestBaseData request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var path = storeId is null ? $"api/v1/payment-requests/{paymentRequestId}" : $"api/v1/stores/{storeId}/payment-requests/{paymentRequestId}";
        return await SendHttpRequest<PaymentRequestBaseData>(path, request, HttpMethod.Put, token);
    }
}
