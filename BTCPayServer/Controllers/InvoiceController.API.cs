using BTCPayServer.Authentication;
using Microsoft.Extensions.Logging;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Servcices.Invoices;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {
		[HttpPost]
		[Route("invoices")]
		[MediaTypeConstraint("application/json")]
		[BitpayAPIConstraint]
		public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] Invoice invoice)
		{
			var bitToken = await CheckTokenPermissionAsync(Facade.Merchant, invoice.Token);
			var store = await FindStore(bitToken);
			return await CreateInvoiceCore(invoice, store);
		}

		[HttpGet]
		[Route("invoices/{id}")]
		[BitpayAPIConstraint]
		public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id, string token)
		{
			var bitToken = await CheckTokenPermissionAsync(Facade.Merchant, token);
			var store = await FindStore(bitToken);
			var invoice = await _InvoiceRepository.GetInvoice(store.Id, id);
			if(invoice == null)
				throw new BitpayHttpException(404, "Object not found");

			var resp = invoice.EntityToDTO(_ExternalUrl);
			return new DataWrapper<InvoiceResponse>(resp);
		}

		[HttpGet]
		[Route("invoices")]
		[BitpayAPIConstraint]
		public async Task<DataWrapper<InvoiceResponse[]>> GetInvoices(
			string token,
			DateTimeOffset? dateStart = null,
			DateTimeOffset? dateEnd = null,
			string orderId = null,
			string itemCode = null,
			string status = null,
			int? limit = null,
			int? offset = null)
		{
			if(dateEnd != null)
				dateEnd = dateEnd.Value + TimeSpan.FromDays(1); //Should include the end day
			var bitToken = await CheckTokenPermissionAsync(Facade.Merchant, token);
			var store = await FindStore(bitToken);
			var query = new InvoiceQuery()
			{
				Count = limit,
				Skip = offset,
				EndDate = dateEnd,
				StartDate = dateStart,
				OrderId = orderId,
				ItemCode = itemCode,
				Status = status,
				StoreId = store.Id
			};


			var entities = (await _InvoiceRepository.GetInvoices(query))
							.Select((o) => o.EntityToDTO(_ExternalUrl)).ToArray();

			return DataWrapper.Create(entities);
		}

		private async Task<BitTokenEntity> CheckTokenPermissionAsync(Facade facade, string expectedToken)
		{
			if(facade == null)
				throw new ArgumentNullException(nameof(facade));

			var actualTokens = (await _TokenRepository.GetTokens(this.GetBitIdentity().SIN)).Where(t => t.Active).ToArray();
			actualTokens = actualTokens.SelectMany(t => GetCompatibleTokens(t)).ToArray();
			
			var actualToken = actualTokens.FirstOrDefault(a => a.Value.Equals(expectedToken, StringComparison.Ordinal));
			if(expectedToken == null || actualToken == null)
			{
				Logs.PayServer.LogDebug($"No token found for facade {facade} for SIN {this.GetBitIdentity().SIN}");
				throw new BitpayHttpException(401, $"This endpoint does not support the `{actualTokens.Select(a => a.Name).Concat(new[] { "user" }).FirstOrDefault()}` facade");
			}
			return actualToken;
		}

		private IEnumerable<BitTokenEntity> GetCompatibleTokens(BitTokenEntity token)
		{
			if(token.Name == Facade.Merchant.ToString())
			{
				yield return token.Clone(Facade.User);
				yield return token.Clone(Facade.PointOfSale);
			}
			if(token.Name == Facade.PointOfSale.ToString())
			{
				yield return token.Clone(Facade.User);
			}
			yield return token;
		}

		private async Task<StoreData> FindStore(BitTokenEntity bitToken)
		{
			var store = await _StoreRepository.FindStore(bitToken.PairedId);
			if(store == null)
				throw new BitpayHttpException(401, "Unknown store");
			return store;
		}

	}
}
