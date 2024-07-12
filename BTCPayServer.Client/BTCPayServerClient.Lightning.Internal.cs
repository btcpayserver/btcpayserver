using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningNodeInformationData>($"api/v1/server/lightning/{cryptoCode}/info", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningNodeBalanceData> GetLightningNodeBalance(string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningNodeBalanceData>($"api/v1/server/lightning/{cryptoCode}/balance", null, HttpMethod.Get, token);
    }

    public virtual async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request,
        CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        await SendHttpRequest($"api/v1/server/lightning/{cryptoCode}/connect", request, HttpMethod.Post, token);
    }

    public virtual async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<LightningChannelData>>($"api/v1/server/lightning/{cryptoCode}/channels", null, HttpMethod.Get, token);
    }

    public virtual async Task OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/server/lightning/{cryptoCode}/channels", request, HttpMethod.Post, token);
    }

    public virtual async Task<string> GetLightningDepositAddress(string cryptoCode, CancellationToken token = default)
    {
        return await SendHttpRequest<string>($"api/v1/server/lightning/{cryptoCode}/address", null, HttpMethod.Post, token);
    }

    public virtual async Task<LightningPaymentData> PayLightningInvoice(string cryptoCode, PayLightningInvoiceRequest request,
        CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<LightningPaymentData>($"api/v1/server/lightning/{cryptoCode}/invoices/pay", request, HttpMethod.Post, token);
    }

    public virtual async Task<LightningPaymentData> GetLightningPayment(string cryptoCode,
        string paymentHash, CancellationToken token = default)
    {
        if (paymentHash == null) throw new ArgumentNullException(nameof(paymentHash));
        return await SendHttpRequest<LightningPaymentData>($"api/v1/server/lightning/{cryptoCode}/payments/{paymentHash}", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode,
        string invoiceId, CancellationToken token = default)
    {
        if (invoiceId == null) throw new ArgumentNullException(nameof(invoiceId));
        return await SendHttpRequest<LightningInvoiceData>($"api/v1/server/lightning/{cryptoCode}/invoices/{invoiceId}", null, HttpMethod.Get, token);
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
        return await SendHttpRequest<LightningInvoiceData[]>($"api/v1/server/lightning/{cryptoCode}/invoices", queryPayload, HttpMethod.Get, token);
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
        return await SendHttpRequest<LightningPaymentData[]>($"api/v1/server/lightning/{cryptoCode}/payments", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode, CreateLightningInvoiceRequest request,
        CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<LightningInvoiceData>($"api/v1/server/lightning/{cryptoCode}/invoices", request, HttpMethod.Post, token);
    }
}
