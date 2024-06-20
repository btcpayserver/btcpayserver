using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<PullPaymentData> CreatePullPayment(string storeId, CreatePullPaymentRequest request, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PullPaymentData>($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments", request, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task<PullPaymentData> GetPullPayment(string pullPaymentId, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PullPaymentData>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}", null, HttpMethod.Get, cancellationToken);
    }

    public virtual async Task<RegisterBoltcardResponse> RegisterBoltcard(string pullPaymentId, RegisterBoltcardRequest request, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<RegisterBoltcardResponse>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/boltcards", request, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task<PullPaymentData[]> GetPullPayments(string storeId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, object> { { "includeArchived", includeArchived } };
        return await SendHttpRequest<PullPaymentData[]>($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments", query, HttpMethod.Get, cancellationToken);
    }

    public virtual async Task ArchivePullPayment(string storeId, string pullPaymentId, CancellationToken cancellationToken = default)
    {
        await SendHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}", null, HttpMethod.Delete, cancellationToken);
    }

    public virtual async Task<PayoutData[]> GetPayouts(string pullPaymentId, bool includeCancelled = false, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, object> { { "includeCancelled", includeCancelled } };
        return await SendHttpRequest<PayoutData[]>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts", query, HttpMethod.Get, cancellationToken);
    }

    public virtual async Task<PayoutData[]> GetStorePayouts(string storeId, bool includeCancelled = false, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, object> { { "includeCancelled", includeCancelled } };
        return await SendHttpRequest<PayoutData[]>($"api/v1/stores/{storeId}/payouts", queryPayload: query, method: HttpMethod.Get, cancellationToken);
    }

    public virtual async Task<PayoutData> CreatePayout(string pullPaymentId, CreatePayoutRequest payoutRequest, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PayoutData>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts", bodyPayload: payoutRequest, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task<PayoutData> GetPullPaymentPayout(string pullPaymentId, string payoutId, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PayoutData>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts/{payoutId}", null, HttpMethod.Get, cancellationToken);
    }

    public virtual async Task<PayoutData> GetStorePayout(string storeId, string payoutId, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PayoutData>($"api/v1/stores/{storeId}/payouts/{payoutId}", null, HttpMethod.Get, cancellationToken);
    }

    public virtual async Task<PayoutData> CreatePayout(string storeId, CreatePayoutThroughStoreRequest payoutRequest, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PayoutData>($"api/v1/stores/{storeId}/payouts", bodyPayload: payoutRequest, method: HttpMethod.Post, cancellationToken);
    }

    public virtual async Task CancelPayout(string storeId, string payoutId, CancellationToken cancellationToken = default)
    {
        await SendHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}", null, HttpMethod.Delete, cancellationToken);
    }

    public virtual async Task<PayoutData> ApprovePayout(string storeId, string payoutId, ApprovePayoutRequest request, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PayoutData>($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}", request, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task MarkPayoutPaid(string storeId, string payoutId, CancellationToken cancellationToken = default)
    {
        await SendHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}/mark-paid", null, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task MarkPayout(string storeId, string payoutId, MarkPayoutRequest request, CancellationToken cancellationToken = default)
    {
        await SendHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}/mark", request, HttpMethod.Post, cancellationToken);
    }

    public virtual async Task<PullPaymentLNURL> GetPullPaymentLNURL(string pullPaymentId, CancellationToken cancellationToken = default)
    {
        return await SendHttpRequest<PullPaymentLNURL>($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/lnurl", null, HttpMethod.Get, cancellationToken);
    }
}
