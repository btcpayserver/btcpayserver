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
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Cors;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Controllers
{
    [EnableCors("BitpayAPI")]
    [BitpayAPIConstraint]
    public class InvoiceControllerAPI : Controller
    {
        private InvoiceController _InvoiceController;
        private InvoiceRepository _InvoiceRepository;
        private TokenRepository _TokenRepository;
        private StoreRepository _StoreRepository;

        public InvoiceControllerAPI(InvoiceController invoiceController,
                                    InvoiceRepository invoceRepository,
                                    TokenRepository tokenRepository,
                                    StoreRepository storeRepository)
        {
            this._InvoiceController = invoiceController;
            this._InvoiceRepository = invoceRepository;
            this._TokenRepository = tokenRepository;
            this._StoreRepository = storeRepository;
        }

        [HttpPost]
        [Route("invoices")]
        [MediaTypeConstraint("application/json")]
        public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] Invoice invoice)
        {
            var bitToken = await CheckTokenPermissionAsync(Facade.Merchant, invoice.Token);
            var store = await FindStore(bitToken);
            return await _InvoiceController.CreateInvoiceCore(invoice, store, HttpContext.Request.GetAbsoluteRoot());
        }

        [HttpGet]
        [Route("invoices/{id}")]
        public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id, string token)
        {
            var bitToken = await CheckTokenPermissionAsync(Facade.Merchant, token);
            var store = await FindStore(bitToken);
            var invoice = await _InvoiceRepository.GetInvoice(store.Id, id);
            if (invoice == null)
                throw new BitpayHttpException(404, "Object not found");

            var resp = invoice.EntityToDTO();
            return new DataWrapper<InvoiceResponse>(resp);
        }

        [HttpGet]
        [Route("invoices")]
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
            if (dateEnd != null)
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
                            .Select((o) => o.EntityToDTO()).ToArray();

            return DataWrapper.Create(entities);
        }

        private async Task<BitTokenEntity> CheckTokenPermissionAsync(Facade facade, string expectedToken)
        {
            if (facade == null)
                throw new ArgumentNullException(nameof(facade));

            var actualTokens = (await _TokenRepository.GetTokens(this.GetBitIdentity().SIN)).ToArray();
            actualTokens = actualTokens.SelectMany(t => GetCompatibleTokens(t)).ToArray();

            var actualToken = actualTokens.FirstOrDefault(a => a.Value.Equals(expectedToken, StringComparison.Ordinal));
            if (expectedToken == null || actualToken == null)
            {
                Logs.PayServer.LogDebug($"No token found for facade {facade} for SIN {this.GetBitIdentity().SIN}");
                throw new BitpayHttpException(401, $"This endpoint does not support the `{actualTokens.Select(a => a.Facade).Concat(new[] { "user" }).FirstOrDefault()}` facade");
            }
            return actualToken;
        }

        private IEnumerable<BitTokenEntity> GetCompatibleTokens(BitTokenEntity token)
        {
            if (token.Facade == Facade.Merchant.ToString())
            {
                yield return token.Clone(Facade.User);
                yield return token.Clone(Facade.PointOfSale);
            }
            if (token.Facade == Facade.PointOfSale.ToString())
            {
                yield return token.Clone(Facade.User);
            }
            yield return token;
        }

        private async Task<StoreData> FindStore(BitTokenEntity bitToken)
        {
            var store = await _StoreRepository.FindStore(bitToken.StoreId);
            if (store == null)
                throw new BitpayHttpException(401, "Unknown store");
            return store;
        }

    }
}
