using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<ApiHealthData> GetHealth(CancellationToken token = default)
    {
        return await SendHttpRequest<ApiHealthData>("api/v1/health", null, HttpMethod.Get, token);
    }
}
