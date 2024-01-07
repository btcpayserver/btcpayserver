using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/server/info"), token);
            return await HandleResponse<ServerInfoData>(response);
        }
        
        public virtual async Task<List<RoleData>> GetServerRoles(CancellationToken token = default)
        {
            using var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/server/roles"), token);
            return await HandleResponse<List<RoleData>>(response);
        }
    }
}
