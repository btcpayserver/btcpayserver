using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<LightningNodeInformationData> GetLightningNodeInfo(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningNodeInformationData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/info", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningNodeBalanceData> GetLightningNodeBalance(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningNodeBalanceData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/balance", null, HttpMethod.Get, token);
    }

    public virtual async Task<HistogramData> GetLightningNodeHistogram(string storeId, string cryptoCode, HistogramType? type = null,
        CancellationToken token = default)
    {
        var queryPayload = type == null ? null : new Dictionary<string, object> { { "type", type.ToString() } };
        return await SendHttpRequest<HistogramData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/histogram", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task ConnectToLightningNode(string storeId, string cryptoCode, ConnectToNodeRequest request,
        CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        await SendHttpRequest($"api/v1/stores/{storeId}/lightning/{cryptoCode}/connect", request, HttpMethod.Post, token);
    }

    public virtual async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<LightningChannelData>>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/channels", null, HttpMethod.Get, token);
    }

    public virtual async Task OpenLightningChannel(string storeId, string cryptoCode, OpenLightningChannelRequest request,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/lightning/{cryptoCode}/channels", request, HttpMethod.Post, token);
    }

    public virtual async Task<string> GetLightningDepositAddress(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<string>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/address", null, HttpMethod.Post, token);
    }

    public virtual async Task<LightningPaymentData> PayLightningInvoice(string storeId, string cryptoCode, PayLightningInvoiceRequest request,
        CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<LightningPaymentData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices/pay", request, HttpMethod.Post, token);
    }

    public virtual async Task<LightningPaymentData> GetLightningPayment(string storeId, string cryptoCode,
        string paymentHash, CancellationToken token = default)
    {
        if (paymentHash == null) throw new ArgumentNullException(nameof(paymentHash));
        return await SendHttpRequest<LightningPaymentData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/payments/{paymentHash}", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningInvoiceData> GetLightningInvoice(string storeId, string cryptoCode,
        string invoiceId, CancellationToken token = default)
    {
        if (invoiceId == null) throw new ArgumentNullException(nameof(invoiceId));
        return await SendHttpRequest<LightningInvoiceData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices/{invoiceId}", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningInvoiceData[]> GetLightningInvoices(string storeId, string cryptoCode,
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
        return await SendHttpRequest<LightningInvoiceData[]>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task<LightningPaymentData[]> GetLightningPayments(string storeId, string cryptoCode,
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
        return await SendHttpRequest<LightningPaymentData[]>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/payments", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task<LightningInvoiceData> CreateLightningInvoice(string storeId, string cryptoCode,
        CreateLightningInvoiceRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<LightningInvoiceData>($"api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices", request, HttpMethod.Post, token);
    }
}
