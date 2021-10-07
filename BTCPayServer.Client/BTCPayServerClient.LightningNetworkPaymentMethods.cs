using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<LightningNetworkPaymentMethodData>>
            GetStoreLightningNetworkPaymentMethods(string storeId, bool? enabled = null,
                CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (enabled != null)
            {
                query.Add(nameof(enabled), enabled);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LightningNetwork",
                        query), token);
            return await HandleResponse<IEnumerable<LightningNetworkPaymentMethodData>>(response);
        }

        public virtual async Task<LightningNetworkPaymentMethodData> GetStoreLightningNetworkPaymentMethod(
            string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}"), token);
            return await HandleResponse<LightningNetworkPaymentMethodData>(response);
        }

        public virtual async Task RemoveStoreLightningNetworkPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}",
                        method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethod(
            string storeId,
            string cryptoCode, UpdateLightningNetworkPaymentMethodRequest paymentMethod,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}",
                    bodyPayload: paymentMethod, method: HttpMethod.Put), token);
            return await HandleResponse<LightningNetworkPaymentMethodData>(response);
        }
    }
}
