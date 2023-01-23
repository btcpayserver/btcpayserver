using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<LightningAddressData[]> GetStoreLightningAddresses(string storeId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/lightning-addresses",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningAddressData[]>(response);
        }

        public virtual async Task<LightningAddressData> GetStoreLightningAddress(string storeId, string username,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/lightning-addresses/{username}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<LightningAddressData>(response);
        }

        public virtual async Task RemoveStoreLightningAddress(string storeId, string username,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/lightning-addresses/{username}",
                    method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<LightningAddressData> AddOrUpdateStoreLightningAddress(string storeId,
            string username, LightningAddressData data,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/lightning-addresses/{username}",
                    method: HttpMethod.Post, bodyPayload: data), token);

            return await HandleResponse<LightningAddressData>(response);
        }
    }
}
