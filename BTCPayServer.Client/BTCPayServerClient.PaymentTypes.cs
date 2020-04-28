using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<string>> GetAvailablePaymentTypes(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/payment-types"), token);
            return await HandleResponse<IEnumerable<string>>(response);
        }

        public virtual async Task<IEnumerable<string>> GetAvailablePaymentMethods(string paymentType, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/payment-types/{paymentType}"), token);
            return await HandleResponse<IEnumerable<string>>(response);
        }
    }
}
