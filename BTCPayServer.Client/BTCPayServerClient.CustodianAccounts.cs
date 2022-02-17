using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<CustodianAccountData>> GetCustodianAccounts(string storeId, bool includeAssetBalances = false, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            if (includeAssetBalances)
            {
                queryPayload.Add("assetBalances", "true");
            }
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts", queryPayload), token);
            return await HandleResponse<IEnumerable<CustodianAccountData>>(response);
        }
        
        public virtual async Task<CustodianAccountData> GetCustodianAccount(string storeId, string accountId, bool includeAssetBalances = false, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            if (includeAssetBalances)
            {
                queryPayload.Add("assetBalances", "true");
            }
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}", queryPayload), token);
            return await HandleResponse<CustodianAccountData>(response);
        }

        public virtual async Task<CustodianAccountData> CreateCustodianAccount(string storeId, CreateCustodianAccountRequest request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts", bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<CustodianAccountData>(response);
        }

        public virtual async Task<CustodianAccountData> UpdateCustodianAccount(string storeId, string accountId, CreateCustodianAccountRequest request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}", bodyPayload: request, method: HttpMethod.Put), token);
            return await HandleResponse<CustodianAccountData>(response);
        }

        public virtual async Task DeleteCustodianAccount(string storeId, string accountId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
