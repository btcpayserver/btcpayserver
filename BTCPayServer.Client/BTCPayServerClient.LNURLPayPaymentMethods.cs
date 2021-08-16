using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<LNURLPayPaymentMethodData>>
            GetStoreLNURLPayPaymentMethods(string storeId, bool? enabled = null,
                CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (enabled != null)
            {
                query.Add(nameof(enabled), enabled);
            }

            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LNURLPay",
                        query), token);
            return await HandleResponse<IEnumerable<LNURLPayPaymentMethodData>>(response);
        }

        public virtual async Task<LNURLPayPaymentMethodData> GetStoreLNURLPayPaymentMethod(
            string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}"), token);
            return await HandleResponse<LNURLPayPaymentMethodData>(response);
        }

        public virtual async Task RemoveStoreLNURLPayPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}",
                        method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<LNURLPayPaymentMethodData> UpdateStoreLNURLPayPaymentMethod(
            string storeId,
            string cryptoCode, LNURLPayPaymentMethodData paymentMethod,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}",
                    bodyPayload: paymentMethod, method: HttpMethod.Put), token);
            return await HandleResponse<LNURLPayPaymentMethodData>(response);
        }
    }
}
