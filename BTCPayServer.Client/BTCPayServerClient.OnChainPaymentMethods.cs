using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<OnChainPaymentMethodPreviewResultData>
        PreviewProposedStoreOnChainPaymentMethodAddresses(
            string storeId, string paymentMethodId, string derivationScheme, int offset = 0,
            int amount = 10,
            CancellationToken token = default)
    {
        return await SendHttpRequest<UpdatePaymentMethodRequest, OnChainPaymentMethodPreviewResultData>($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/preview",
            new Dictionary<string, object> { { "offset", offset }, { "amount", amount } },
            new UpdatePaymentMethodRequest { Config = JValue.CreateString(derivationScheme) },
            HttpMethod.Post, token);
    }

    public virtual async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
        string storeId, string paymentMethodId, int offset = 0, int amount = 10,
        CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainPaymentMethodPreviewResultData>($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/preview",
            new Dictionary<string, object> { { "offset", offset }, { "amount", amount } }, HttpMethod.Get, token);
    }

    public virtual async Task<GenerateOnChainWalletResponse> GenerateOnChainWallet(string storeId,
        string paymentMethodId, GenerateOnChainWalletRequest request,
        CancellationToken token = default)
    {
        return await SendHttpRequest<GenerateOnChainWalletResponse>($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/generate", request, HttpMethod.Post, token);
    }

}
