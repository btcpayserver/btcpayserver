using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
    {
        return await SendHttpRequest<ServerInfoData>("api/v1/server/info", null, HttpMethod.Get, token);
    }
        
    public virtual async Task<List<RoleData>> GetServerRoles(CancellationToken token = default)
    {
        return await SendHttpRequest<List<RoleData>>("api/v1/server/roles", null, HttpMethod.Get, token);
    }
}
