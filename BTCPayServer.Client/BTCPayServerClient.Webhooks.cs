using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<StoreWebhookData> CreateWebhook(string storeId, CreateStoreWebhookRequest create, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreWebhookData>($"api/v1/stores/{storeId}/webhooks", create, HttpMethod.Post, token);
    }

    public virtual async Task<StoreWebhookData> GetWebhook(string storeId, string webhookId, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}"), token);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await HandleResponse<StoreWebhookData>(response);
    }

    public virtual async Task<StoreWebhookData> UpdateWebhook(string storeId, string webhookId, UpdateStoreWebhookRequest update, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreWebhookData>($"api/v1/stores/{storeId}/webhooks/{webhookId}", update, HttpMethod.Put, token);
    }

    public virtual async Task<bool> DeleteWebhook(string storeId, string webhookId, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}", method: HttpMethod.Delete), token);
        return response.IsSuccessStatusCode;
    }

    public virtual async Task<StoreWebhookData[]> GetWebhooks(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreWebhookData[]>($"api/v1/stores/{storeId}/webhooks", null, HttpMethod.Get, token);
    }

    public virtual async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId, CancellationToken token = default)
    {
        return await SendHttpRequest<WebhookDeliveryData[]>($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries", null, HttpMethod.Get, token);
    }

    public virtual async Task<WebhookDeliveryData> GetWebhookDelivery(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}"), token);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await HandleResponse<WebhookDeliveryData>(response);
    }

    public virtual async Task<string> RedeliverWebhook(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
    {
        return await SendHttpRequest<string>($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver", null, HttpMethod.Post, token);
    }

    public virtual async Task<WebhookEvent> GetWebhookDeliveryRequest(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
    {
        var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request"), token);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await HandleResponse<WebhookEvent>(response);
    }
}
