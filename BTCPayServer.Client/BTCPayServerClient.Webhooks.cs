using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public async Task<StoreWebhookData> CreateWebhook(string storeId, Client.Models.CreateStoreWebhookRequest create, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks", bodyPayload: create, method: HttpMethod.Post), token);
            return await HandleResponse<StoreWebhookData>(response);
        }
        public async Task<StoreWebhookData> GetWebhook(string storeId, string webhookId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}"), token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            return await HandleResponse<StoreWebhookData>(response);
        }
        public async Task<StoreWebhookData> UpdateWebhook(string storeId, string webhookId, Models.UpdateStoreWebhookRequest update, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}", bodyPayload: update, method: HttpMethod.Put), token);
            return await HandleResponse<StoreWebhookData>(response);
        }
        public async Task<bool> DeleteWebhook(string storeId, string webhookId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}", method: HttpMethod.Delete), token);
            return response.IsSuccessStatusCode;
        }
        public async Task<StoreWebhookData[]> GetWebhooks(string storeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks"), token);
            return await HandleResponse<StoreWebhookData[]>(response);
        }
        public async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries"), token);
            return await HandleResponse<WebhookDeliveryData[]>(response);
        }
        public async Task<WebhookDeliveryData> GetWebhookDelivery(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}"), token);
            return await HandleResponse<WebhookDeliveryData>(response);
        }
        public async Task<string> RedeliverWebhook(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/redeliver", null, HttpMethod.Post), token);
            return await HandleResponse<string>(response);
        }

        public async Task<WebhookEvent> GetWebhookDeliveryRequest(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/webhooks/{webhookId}/deliveries/{deliveryId}/request"), token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            return await HandleResponse<WebhookEvent>(response);
        }
    }
}
