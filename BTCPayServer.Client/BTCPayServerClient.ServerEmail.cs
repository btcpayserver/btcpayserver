using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<ServerEmailSettingsData> GetServerEmailSettings(CancellationToken token = default)
    {
        return await SendHttpRequest<ServerEmailSettingsData>("api/v1/server/email", null, HttpMethod.Get, token);
    }
    
    public virtual async Task<ServerEmailSettingsData> UpdateServerEmailSettings(ServerEmailSettingsData request, CancellationToken token = default)
    {
        return await SendHttpRequest<ServerEmailSettingsData>("api/v1/server/email", request, HttpMethod.Put, token);
    }
}
