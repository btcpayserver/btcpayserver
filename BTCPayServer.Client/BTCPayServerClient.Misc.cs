using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<PermissionMetadata[]> GetPermissionMetadata(CancellationToken token = default)
    {
        return await SendHttpRequest<PermissionMetadata[]>("misc/permissions", null, HttpMethod.Get, token);
    }

    public virtual async Task<Language[]> GetAvailableLanguages(CancellationToken token = default)
    {
        return await SendHttpRequest<Language[]>("misc/lang", null, HttpMethod.Get, token);
    }
}
