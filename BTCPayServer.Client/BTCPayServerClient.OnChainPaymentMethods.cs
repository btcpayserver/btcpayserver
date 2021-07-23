using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<OnChainPaymentMethodData>> GetStoreOnChainPaymentMethods(string storeId,
            bool enabledOnly = false,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain",
                        new Dictionary<string, object>() {{nameof(enabledOnly), enabledOnly}}), token);
            return await HandleResponse<IEnumerable<OnChainPaymentMethodData>>(response);
        }

        public virtual async Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}"), token);
            return await HandleResponse<OnChainPaymentMethodData>(response);
        }

        public virtual async Task RemoveStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}",
                        method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, OnChainPaymentMethodData paymentMethod,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}",
                    bodyPayload: paymentMethod, method: HttpMethod.Put), token);
            return await HandleResponse<OnChainPaymentMethodData>(response);
        }

        public virtual async Task<OnChainPaymentMethodPreviewResultData>
            PreviewProposedStoreOnChainPaymentMethodAddresses(
                string storeId, string cryptoCode, OnChainPaymentMethodData paymentMethod, int offset = 0,
                int amount = 10,
                CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/preview",
                    bodyPayload: paymentMethod,
                    queryPayload: new Dictionary<string, object>() {{"offset", offset}, {"amount", amount}},
                    method: HttpMethod.Post), token);
            return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
        }

        public virtual async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/preview",
                    queryPayload: new Dictionary<string, object>() {{"offset", offset}, {"amount", amount}},
                    method: HttpMethod.Get), token);
            return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
        }
    }
}
