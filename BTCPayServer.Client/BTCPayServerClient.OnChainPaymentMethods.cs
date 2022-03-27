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
            bool? enabled = null,
            CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (enabled != null)
            {
                query.Add(nameof(enabled), enabled);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain",
                        query), token);
            return await HandleResponse<IEnumerable<OnChainPaymentMethodData>>(response);
        }

        public virtual async Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}"), token);
            return await HandleResponse<OnChainPaymentMethodData>(response);
        }

        public virtual async Task RemoveStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}",
                        method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, UpdateOnChainPaymentMethodRequest paymentMethod,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}",
                    bodyPayload: paymentMethod, method: HttpMethod.Put), token);
            return await HandleResponse<OnChainPaymentMethodData>(response);
        }

        public virtual async Task<OnChainPaymentMethodPreviewResultData>
            PreviewProposedStoreOnChainPaymentMethodAddresses(
                string storeId, string cryptoCode, UpdateOnChainPaymentMethodRequest paymentMethod, int offset = 0,
                int amount = 10,
                CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/preview",
                    bodyPayload: paymentMethod,
                    queryPayload: new Dictionary<string, object>() { { "offset", offset }, { "amount", amount } },
                    method: HttpMethod.Post), token);
            return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
        }

        public virtual async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/preview",
                    queryPayload: new Dictionary<string, object>() { { "offset", offset }, { "amount", amount } },
                    method: HttpMethod.Get), token);
            return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
        }

        public virtual async Task<OnChainPaymentMethodDataWithSensitiveData> GenerateOnChainWallet(string storeId,
            string cryptoCode, GenerateOnChainWalletRequest request,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/generate",
                    bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<OnChainPaymentMethodDataWithSensitiveData>(response);
        }

    }
}
