using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using NBitcoin;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<OnChainWalletOverviewData> ShowOnChainWalletOverview(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainWalletOverviewData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet", null, HttpMethod.Get, token);
    }
    
    public virtual async Task<HistogramData> GetOnChainWalletHistogram(string storeId, string cryptoCode, HistogramType? type = null,
        CancellationToken token = default)
    {
        var queryPayload = type == null ? null : new Dictionary<string, object> { { "type", type.ToString() } };
        return await SendHttpRequest<HistogramData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/histogram", queryPayload, HttpMethod.Get, token);
    }
    
    public virtual async Task<OnChainWalletFeeRateData> GetOnChainFeeRate(string storeId, string cryptoCode, int? blockTarget = null,
        CancellationToken token = default)
    {
        var queryParams = new Dictionary<string, object>();
        if (blockTarget != null)
        {
            queryParams.Add("blockTarget", blockTarget);
        }
        return await SendHttpRequest<OnChainWalletFeeRateData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/feerate", queryParams, HttpMethod.Get, token);
    }

    public virtual async Task<OnChainWalletAddressData> GetOnChainWalletReceiveAddress(string storeId, string cryptoCode, bool forceGenerate = false,
        CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainWalletAddressData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/address", new Dictionary<string, object>
        {
            {"forceGenerate", forceGenerate}
        }, HttpMethod.Get, token);
    }

    public virtual async Task UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/address", null, HttpMethod.Delete, token);
    }

    public virtual async Task<IEnumerable<OnChainWalletTransactionData>> ShowOnChainWalletTransactions(
        string storeId, string cryptoCode, TransactionStatus[] statusFilter = null, string labelFilter = null, int skip = 0,
        CancellationToken token = default)
    {
        var query = new Dictionary<string, object>();
        if (statusFilter?.Any() is true)
        {
            query.Add(nameof(statusFilter), statusFilter);
        }
        if (labelFilter != null)
        {
            query.Add(nameof(labelFilter), labelFilter);
        }
        if (skip != 0)
        {
            query.Add(nameof(skip), skip);
        }
        return await SendHttpRequest<IEnumerable<OnChainWalletTransactionData>>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/transactions", query, HttpMethod.Get, token);
    }

    public virtual async Task<OnChainWalletTransactionData> GetOnChainWalletTransaction(
        string storeId, string cryptoCode, string transactionId,
        CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainWalletTransactionData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/transactions/{transactionId}", null, HttpMethod.Get, token);
    }

    public virtual async Task<OnChainWalletTransactionData> PatchOnChainWalletTransaction(
        string storeId, string cryptoCode, string transactionId,
        PatchOnChainTransactionRequest request,
        bool force = false, CancellationToken token = default)
    {
        return await SendHttpRequest<PatchOnChainTransactionRequest, OnChainWalletTransactionData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/transactions/{transactionId}", 
            new Dictionary<string, object> { {"force", force} }, request, HttpMethod.Patch, token);
    }

    public virtual async Task<IEnumerable<OnChainWalletUTXOData>> GetOnChainWalletUTXOs(string storeId,
        string cryptoCode,
        CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<OnChainWalletUTXOData>>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/utxos", null, HttpMethod.Get, token);
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
        return await SendHttpRequest<OnChainWalletTransactionData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/transactions", request, HttpMethod.Post, token);
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
        return Transaction.Parse(await SendHttpRequest<string>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/transactions", request, HttpMethod.Post, token), network);
    }
}
