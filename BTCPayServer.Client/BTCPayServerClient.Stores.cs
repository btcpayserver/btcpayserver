using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<StoreData>> GetStores(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/stores"), token);
            return await HandleResponse<IEnumerable<StoreData>>(response);
        }
    }
}
