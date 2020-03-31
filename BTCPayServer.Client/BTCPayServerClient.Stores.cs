using System.Collections.Generic;
using System.Net.Http;
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

        public virtual async Task<StoreData> GetStore(string storeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}"), token);
            return await HandleResponse<StoreData>(response);
        }
        
        public virtual async Task RemoveStore(string storeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}", method: HttpMethod.Delete), token);
            HandleResponse(response);
        }
    }
}
