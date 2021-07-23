using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<PermissionMetadata[]> GetPermissionMetadata(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("misc/permissions"), token);
            return await HandleResponse<PermissionMetadata[]>(response);
        }
        public virtual async Task<Language[]> GetAvailableLanguages(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("misc/lang"), token);
            return await HandleResponse<Language[]>(response);
        }
    }
}
