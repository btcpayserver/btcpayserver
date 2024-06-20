using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<PullPaymentData> CreatePullPayment(string storeId, CreatePullPaymentRequest request, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments", bodyPayload: request, method: HttpMethod.Post), cancellationToken);
            return await HandleResponse<PullPaymentData>(response);
        }
        public virtual async Task<PullPaymentData> GetPullPayment(string pullPaymentId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}", method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PullPaymentData>(response);
        }

        public virtual async Task<RegisterBoltcardResponse> RegisterBoltcard(string pullPaymentId, RegisterBoltcardRequest request, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/boltcards", bodyPayload: request, method: HttpMethod.Post), cancellationToken);
            return await HandleResponse<RegisterBoltcardResponse>(response);
        }

        public virtual async Task<PullPaymentData[]> GetPullPayments(string storeId, bool includeArchived = false, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> query = new Dictionary<string, object>();
            query.Add("includeArchived", includeArchived);
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments", queryPayload: query, method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PullPaymentData[]>(response);
        }

        public virtual async Task ArchivePullPayment(string storeId, string pullPaymentId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}", method: HttpMethod.Delete), cancellationToken);
            await HandleResponse(response);
        }

        public virtual async Task<PayoutData[]> GetPayouts(string pullPaymentId, bool includeCancelled = false, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> query = new Dictionary<string, object>();
            query.Add("includeCancelled", includeCancelled);
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts", queryPayload: query, method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PayoutData[]>(response);
        }
        public virtual async Task<PayoutData[]> GetStorePayouts(string storeId, bool includeCancelled = false, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> query = new Dictionary<string, object>();
            query.Add("includeCancelled", includeCancelled);
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payouts", queryPayload: query, method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PayoutData[]>(response);
        }
        public virtual async Task<PayoutData> CreatePayout(string pullPaymentId, CreatePayoutRequest payoutRequest, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts", bodyPayload: payoutRequest, method: HttpMethod.Post), cancellationToken);
            return await HandleResponse<PayoutData>(response);
        }
        public virtual async Task<PayoutData> GetPullPaymentPayout(string pullPaymentId, string payoutId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/payouts/{payoutId}", method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PayoutData>(response);
        }
        public virtual async Task<PayoutData> GetStorePayout(string storeId, string payoutId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payouts/{payoutId}", method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PayoutData>(response);
        }
        public virtual async Task<PayoutData> CreatePayout(string storeId, CreatePayoutThroughStoreRequest payoutRequest, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payouts", bodyPayload: payoutRequest, method: HttpMethod.Post), cancellationToken);
            return await HandleResponse<PayoutData>(response);
        }
        public virtual async Task CancelPayout(string storeId, string payoutId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}", method: HttpMethod.Delete), cancellationToken);
            await HandleResponse(response);
        }
        public virtual async Task<PayoutData> ApprovePayout(string storeId, string payoutId, ApprovePayoutRequest request, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}", bodyPayload: request, method: HttpMethod.Post), cancellationToken);
            return await HandleResponse<PayoutData>(response);
        }

        public virtual async Task MarkPayoutPaid(string storeId, string payoutId,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest(
                    $"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}/mark-paid",
                    method: HttpMethod.Post), cancellationToken);
            await HandleResponse(response);
        }
        public virtual async Task MarkPayout(string storeId, string payoutId, MarkPayoutRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest(
                    $"api/v1/stores/{HttpUtility.UrlEncode(storeId)}/payouts/{HttpUtility.UrlEncode(payoutId)}/mark",
                    method: HttpMethod.Post, bodyPayload: request), cancellationToken);
            await HandleResponse(response);
        }

        public virtual async Task<PullPaymentLNURL> GetPullPaymentLNURL(string pullPaymentId,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest(
                    $"api/v1/pull-payments/{HttpUtility.UrlEncode(pullPaymentId)}/lnurl",
                    method: HttpMethod.Get), cancellationToken);
            return await HandleResponse<PullPaymentLNURL>(response);
        }
    }
}
