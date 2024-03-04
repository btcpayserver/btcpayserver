using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<GenericPaymentMethodData> UpdateStorePaymentMethod(
            string storeId,
            string paymentMethodId,
            UpdatePaymentMethodRequest request,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}", bodyPayload: request, method: HttpMethod.Put),
                    token);
            return await HandleResponse<GenericPaymentMethodData>(response);
        }
        public virtual async Task RemoveStorePaymentMethod(string storeId, string paymentMethodId)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}", method: HttpMethod.Delete),
                    CancellationToken.None);
            await HandleResponse(response);
        }

        public virtual async Task<GenericPaymentMethodData> GetStorePaymentMethod(string storeId,
            string paymentMethodId, bool? includeConfig = null, CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (includeConfig != null)
            {
                query.Add(nameof(includeConfig), includeConfig);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}",
                        query), token);
            return await HandleResponse<GenericPaymentMethodData>(response);
        }
        public virtual async Task<GenericPaymentMethodData[]> GetStorePaymentMethods(string storeId,
            bool? onlyEnabled = null, bool? includeConfig = null, CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (onlyEnabled != null)
            {
                query.Add(nameof(onlyEnabled), onlyEnabled);
            }
            if (includeConfig != null)
            {
                query.Add(nameof(includeConfig), includeConfig);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods",
                        query), token);
            return await HandleResponse<GenericPaymentMethodData[]>(response);
        }
    }
}
