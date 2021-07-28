using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<Dictionary<string, GenericPaymentMethodData>> GetStorePaymentMethods(string storeId,
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
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods",
                        query), token);
            return await HandleResponse<Dictionary<string, GenericPaymentMethodData>>(response);
        }
    }
}
