#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public async Task<OfferingModel> CreateOffering(string storeId, OfferingModel offering, CancellationToken token = default)
    => await SendHttpRequest<OfferingModel>($"api/v1/stores/{storeId}/offerings", offering, HttpMethod.Post, token);
    public async Task<OfferingPlanModel> CreateOfferingPlan(string storeId, string offeringId, CreatePlanRequest request, CancellationToken token = default)
    => await SendHttpRequest<OfferingPlanModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/plans", request, HttpMethod.Post, token);
    public async Task<OfferingPlanModel> GetOfferingPlan(string storeId, string offeringId, string planId, CancellationToken token = default)
    => await SendHttpRequest<OfferingPlanModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/plans/{planId}", null, HttpMethod.Get, token);

    public async Task<OfferingModel> GetOffering(string storeId, string offeringId, CancellationToken token = default)
        => await SendHttpRequest<OfferingModel>($"api/v1/stores/{storeId}/offerings/{offeringId}", null, HttpMethod.Get, token);
    public async Task<OfferingModel[]> GetOfferings(string storeId, CancellationToken token = default)
        => await SendHttpRequest<OfferingModel[]>($"api/v1/stores/{storeId}/offerings", null, HttpMethod.Get, token);
    public async Task<PlanCheckoutModel> CreatePlanCheckout(CreatePlanCheckoutRequest request, CancellationToken token = default)
        => await SendHttpRequest<PlanCheckoutModel>($"api/v1/plan-checkout", request, HttpMethod.Post, token);
    public async Task<PlanCheckoutModel> GetPlanCheckout(string checkoutId, CancellationToken token = default)
        => await SendHttpRequest<PlanCheckoutModel>($"api/v1/plan-checkout/{checkoutId}", null, HttpMethod.Get, token);

    public async Task<CreditModel> GetCredit(string storeId, string offeringId, string customerSelector, string currency, CancellationToken token = default)
        => await SendHttpRequest<CreditModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{Uri.EscapeDataString(customerSelector)}/credits/{currency}", null, HttpMethod.Get, token);

    public async Task<CreditModel> UpdateCredit(string storeId, string offeringId, string customerSelector, string currency, UpdateCreditRequest request, CancellationToken token = default)
        => await SendHttpRequest<CreditModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{Uri.EscapeDataString(customerSelector)}/credits/{currency}", request, HttpMethod.Post, token);

    public async Task<PlanCheckoutModel> ProceedPlanCheckout(string checkoutId, string? email = null, CancellationToken token = default)
    {
        if (email is not null)
            return await SendHttpRequest<PlanCheckoutModel>($"api/v1/plan-checkout/{checkoutId}?email={Uri.EscapeDataString(email)}", null, HttpMethod.Post, token);
        else
            return await SendHttpRequest<PlanCheckoutModel>($"api/v1/plan-checkout/{checkoutId}", null, HttpMethod.Post, token);
    }
    public async Task<SubscriberModel> GetSubscriber(string storeId, string offeringId, string customerSelector, CancellationToken token = default)
    => await SendHttpRequest<SubscriberModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{Uri.EscapeDataString(customerSelector)}", null, HttpMethod.Get, token);

    public async Task<SubscriberModel> SuspendSubscriber(string storeId, string offeringId, string customerSelector, string? reason = null,
        CancellationToken token = default)
        => await SendHttpRequest<SubscriberModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{Uri.EscapeDataString(customerSelector)}/suspend", new SuspendSubscriberRequest()
            {
                Reason = reason
            },
            HttpMethod.Post, token);
    public async Task<SubscriberModel> UnsuspendSubscriber(string storeId, string offeringId, string customerSelector, CancellationToken token = default)
        => await SendHttpRequest<SubscriberModel>($"api/v1/stores/{storeId}/offerings/{offeringId}/subscribers/{customerSelector}/unsuspend", null, HttpMethod.Post, token);

    public async Task<PortalSessionModel> CreatePortalSession(CreatePortalSessionRequest request, CancellationToken token = default)
        => await SendHttpRequest<PortalSessionModel>($"api/v1/subscriber-portal", request, HttpMethod.Post, token);
    public async Task<PortalSessionModel> GetPortalSession(string portalSessionId, CancellationToken token = default)
        => await SendHttpRequest<PortalSessionModel>($"api/v1/subscriber-portal/{portalSessionId}", null, HttpMethod.Get, token);
}
