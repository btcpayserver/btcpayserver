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

        public virtual async Task<CustodianAccountResponse> GetCustodianAccount(string storeId, string accountId, bool includeAssetBalances = false, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            if (includeAssetBalances)
            {
                queryPayload.Add("assetBalances", "true");
            }

            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}", queryPayload), token);
            return await HandleResponse<CustodianAccountResponse>(response);
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

        public virtual async Task<DepositAddressData> GetCustodianAccountDepositAddress(string storeId, string accountId, string paymentMethod, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/addresses/{paymentMethod}"), token);
            return await HandleResponse<DepositAddressData>(response);
        }

        public virtual async Task<MarketTradeResponseData> MarketTradeCustodianAccountAsset(string storeId, string accountId, TradeRequestData request, CancellationToken token = default)
        {
            //var response = await _httpClient.SendAsync(CreateHttpRequest("api/v1/users", null, request, HttpMethod.Post), token);
            //return await HandleResponse<ApplicationUserData>(response);
            var internalRequest = CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/market", null,
                    request, HttpMethod.Post);
            var response = await _httpClient.SendAsync(internalRequest, token);
            return await HandleResponse<MarketTradeResponseData>(response);
        }

        public virtual async Task<MarketTradeResponseData> GetCustodianAccountTradeInfo(string storeId, string accountId, string tradeId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/{tradeId}", method: HttpMethod.Get), token);
            return await HandleResponse<MarketTradeResponseData>(response);
        }

        public virtual async Task<TradeQuoteResponseData> GetCustodianAccountTradeQuote(string storeId, string accountId, string fromAsset, string toAsset, CancellationToken token = default)
        {
            var queryPayload = new Dictionary<string, object>();
            queryPayload.Add("fromAsset", fromAsset);
            queryPayload.Add("toAsset", toAsset);
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/trades/quote", queryPayload), token);
            return await HandleResponse<TradeQuoteResponseData>(response);
        }

        public virtual async Task<WithdrawalResponseData> CreateCustodianAccountWithdrawal(string storeId, string accountId, WithdrawRequestData request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals", bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<WithdrawalResponseData>(response);
        }

        public virtual async Task<WithdrawalSimulationResponseData> SimulateCustodianAccountWithdrawal(string storeId, string accountId, WithdrawRequestData request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals/simulation", bodyPayload: request, method: HttpMethod.Post), token);
            return await HandleResponse<WithdrawalSimulationResponseData>(response);
        }

        public virtual async Task<WithdrawalResponseData> GetCustodianAccountWithdrawalInfo(string storeId, string accountId, string paymentMethod, string withdrawalId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/custodian-accounts/{accountId}/withdrawals/{paymentMethod}/{withdrawalId}", method: HttpMethod.Get), token);
            return await HandleResponse<WithdrawalResponseData>(response);
        }
    }
}
