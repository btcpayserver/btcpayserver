using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task SendEmail(string storeId, SendEmailRequest request,
            CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/email/send", bodyPayload: request, method: HttpMethod.Post),
                token);
            await HandleResponse(response);
        }
    }
}
