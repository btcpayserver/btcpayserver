using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using NBitcoin;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<OnChainWalletOverviewData> ShowOnChainWalletOverview(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet"), token);
            return await HandleResponse<OnChainWalletOverviewData>(response);
        }
        
        public virtual async Task<OnChainWalletAddressData> GetOnChainWalletReceiveAddress(string storeId, string cryptoCode, bool forceGenerate = false,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/address", new Dictionary<string, object>()
                    {
                        {"forceGenerate", forceGenerate}
                    }), token);
            return await HandleResponse<OnChainWalletAddressData>(response);
        }
        
        public virtual async Task UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/address",method:HttpMethod.Delete), token);
            await HandleResponse(response);
        }
       
        public virtual async Task<IEnumerable<OnChainWalletTransactionData>> ShowOnChainWalletTransactions(
            string storeId, string cryptoCode, TransactionStatus[] statusFilter = null,
            CancellationToken token = default)
        {
            var query = new Dictionary<string, object>();
            if (statusFilter?.Any() is true)
            {
                query.Add(nameof(statusFilter), statusFilter);
            }
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/transactions", query), token);
            return await HandleResponse<IEnumerable<OnChainWalletTransactionData>>(response);
        }
        
        public virtual async Task<OnChainWalletTransactionData> GetOnChainWalletTransaction(
            string storeId, string cryptoCode, string transactionId,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/transactions/{transactionId}"), token);
            return await HandleResponse<OnChainWalletTransactionData>(response);
        }
        
        public virtual async Task<IEnumerable<OnChainWalletUTXOData>> GetOnChainWalletUTXOs(string storeId,
            string cryptoCode,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/utxos"), token);
            return await HandleResponse<IEnumerable<OnChainWalletUTXOData>>(response);
        }
        
        public virtual async Task<OnChainWalletTransactionData> CreateOnChainTransaction(string storeId,
            string cryptoCode, CreateOnChainTransactionRequest request,
            CancellationToken token = default)
        {
            if (!request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransactionButDoNotBroadcast when wanting to only create the transaction");
            }
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/transactions", null, request, HttpMethod.Post), token);
            return await HandleResponse<OnChainWalletTransactionData>(response);
        }
        
        public virtual async Task<Transaction> CreateOnChainTransactionButDoNotBroadcast(string storeId,
            string cryptoCode, CreateOnChainTransactionRequest request, Network network,
            CancellationToken token = default)
        {
            if (request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransaction when wanting to also broadcast the transaction");
            }
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/Onchain/{cryptoCode}/wallet/transactions", null, request, HttpMethod.Post), token);
            return Transaction.Parse(await HandleResponse<string>(response), network);
        }
    }
}
