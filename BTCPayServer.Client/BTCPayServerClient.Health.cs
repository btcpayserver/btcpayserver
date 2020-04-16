using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<bool> IsHealthy(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/health"), token);
            return response.IsSuccessStatusCode;
        }
    }
}
