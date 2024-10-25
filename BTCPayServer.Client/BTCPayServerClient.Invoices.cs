using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using NBitcoin;

namespace BTCPayServer.Client;

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
        var queryPayload = new Dictionary<string, object> { { nameof(includeArchived), includeArchived } };
        if (startDate is { } s)
            queryPayload.Add(nameof(startDate), Utils.DateTimeToUnixTime(s));
        if (endDate is { } e)
            queryPayload.Add(nameof(endDate), Utils.DateTimeToUnixTime(e));
        if (orderId != null)
            queryPayload.Add(nameof(orderId), orderId);
        if (textSearch != null)
            queryPayload.Add(nameof(textSearch), textSearch);
        if (status != null)
            queryPayload.Add(nameof(status), status.Select(s => s.ToString().ToLower()).ToArray());
        if (skip != null)
            queryPayload.Add(nameof(skip), skip);
        if (take != null)
            queryPayload.Add(nameof(take), take);

        return await SendHttpRequest<IEnumerable<InvoiceData>>($"api/v1/stores/{storeId}/invoices", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task<InvoiceData> GetInvoice(string storeId, string invoiceId,
        CancellationToken token = default)
    {
        return await SendHttpRequest<InvoiceData>($"api/v1/stores/{storeId}/invoices/{invoiceId}", null, HttpMethod.Get, token);
    }
    public virtual async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId, string invoiceId,
        bool onlyAccountedPayments = true, bool includeSensitive = false,
        CancellationToken token = default)
    {
        var queryPayload = new Dictionary<string, object>
        {
            { nameof(onlyAccountedPayments), onlyAccountedPayments },
            { nameof(includeSensitive), includeSensitive }
        };
        return await SendHttpRequest<InvoicePaymentMethodDataModel[]>($"api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task ArchiveInvoice(string storeId, string invoiceId,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}", null, HttpMethod.Delete, token);
    }

    public virtual async Task<InvoiceData> CreateInvoice(string storeId,
        CreateInvoiceRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<InvoiceData>($"api/v1/stores/{storeId}/invoices", request, HttpMethod.Post, token);
    }

    public virtual async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId,
        UpdateInvoiceRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<InvoiceData>($"api/v1/stores/{storeId}/invoices/{invoiceId}", request, HttpMethod.Put, token);
    }

    public virtual async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId,
        MarkInvoiceStatusRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Status != InvoiceStatus.Settled && request.Status != InvoiceStatus.Invalid) throw new ArgumentOutOfRangeException(nameof(request.Status), "Status can only be Invalid or Complete");
        return await SendHttpRequest<InvoiceData>($"api/v1/stores/{storeId}/invoices/{invoiceId}/status", request, HttpMethod.Post, token);
    }

    public virtual async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
    {
        return await SendHttpRequest<InvoiceData>($"api/v1/stores/{storeId}/invoices/{invoiceId}/unarchive", null, HttpMethod.Post, token);
    }

    public virtual async Task ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods/{paymentMethod}/activate", null, HttpMethod.Post, token);
    }

    public virtual async Task<PullPaymentData> RefundInvoice(
        string storeId,
        string invoiceId,
        RefundInvoiceRequest request,
        CancellationToken token = default
    )
    {
        return await SendHttpRequest<PullPaymentData>($"api/v1/stores/{storeId}/invoices/{invoiceId}/refund", request, HttpMethod.Post, token);
    }
}
