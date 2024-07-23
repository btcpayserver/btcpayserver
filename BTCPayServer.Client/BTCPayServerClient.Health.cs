using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<ApiHealthData> GetHealth(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/health"), token);
            return await HandleResponse<ApiHealthData>(response);
        }
    }
}
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp