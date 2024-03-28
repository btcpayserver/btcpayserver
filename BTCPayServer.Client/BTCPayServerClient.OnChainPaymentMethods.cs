using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client
{
	public partial class BTCPayServerClient
	{
		public virtual async Task<OnChainPaymentMethodPreviewResultData>
			PreviewProposedStoreOnChainPaymentMethodAddresses(
				string storeId, string paymentMethodId, string derivationScheme, int offset = 0,
				int amount = 10,
				CancellationToken token = default)
		{
			var response = await _httpClient.SendAsync(
				CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/preview",
					bodyPayload: new UpdatePaymentMethodRequest() { Config = JValue.CreateString(derivationScheme) },
					queryPayload: new Dictionary<string, object>() { { "offset", offset }, { "amount", amount } },
					method: HttpMethod.Post), token);
			return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
		}

		public virtual async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
			string storeId, string paymentMethodId, int offset = 0, int amount = 10,
			CancellationToken token = default)
		{
			var response = await _httpClient.SendAsync(
				CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/preview",
					queryPayload: new Dictionary<string, object>() { { "offset", offset }, { "amount", amount } },
					method: HttpMethod.Get), token);
			return await HandleResponse<OnChainPaymentMethodPreviewResultData>(response);
		}

		public virtual async Task<GenerateOnChainWalletResponse> GenerateOnChainWallet(string storeId,
			string paymentMethodId, GenerateOnChainWalletRequest request,
			CancellationToken token = default)
		{
			var response = await _httpClient.SendAsync(
				CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/generate",
					bodyPayload: request,
					method: HttpMethod.Post), token);
			return await HandleResponse<GenerateOnChainWalletResponse>(response);
		}

	}
}
