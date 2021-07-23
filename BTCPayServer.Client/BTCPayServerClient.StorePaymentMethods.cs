using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<Dictionary<string, GenericPaymentMethodData>> GetStorePaymentMethods(string storeId,
            bool enabledOnly = false,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods",
                        new Dictionary<string, object>() {{nameof(enabledOnly), enabledOnly}}), token);
            return await HandleResponse<Dictionary<string, GenericPaymentMethodData>>(response);
        }
    }
}
