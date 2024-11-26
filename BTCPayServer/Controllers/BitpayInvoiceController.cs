using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint]
    [Authorize(Policies.CanCreateInvoice, AuthenticationSchemes = AuthenticationSchemes.Bitpay)]
    public class BitpayInvoiceController : Controller
    {
        private readonly UIInvoiceController _InvoiceController;
        private readonly Dictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension> _bitpayExtensions;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly InvoiceRepository _InvoiceRepository;

        public BitpayInvoiceController(UIInvoiceController invoiceController,
                                    Dictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension> bitpayExtensions,
                                    CurrencyNameTable currencyNameTable,
                                    InvoiceRepository invoiceRepository)
        {
            _InvoiceController = invoiceController;
            _bitpayExtensions = bitpayExtensions;
            _currencyNameTable = currencyNameTable;
            _InvoiceRepository = invoiceRepository;
        }

        [HttpPost]
        [Route("invoices")]
        [MediaTypeConstraint("application/json")]
        public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] BitpayCreateInvoiceRequest invoice, CancellationToken cancellationToken)
        {
            if (invoice == null)
                throw new BitpayHttpException(400, "Invalid invoice");
            return await CreateInvoiceCore(invoice, HttpContext.GetStoreData(), HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);
        }

        [HttpGet]
        [Route("invoices/{id}")]
        public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] { id },
                StoreId = new[] { HttpContext.GetStoreData().Id }
            })).FirstOrDefault();
            if (invoice == null)
                throw new BitpayHttpException(404, "Object not found");
            return new DataWrapper<InvoiceResponse>(invoice.EntityToDTO(_bitpayExtensions, Url, _currencyNameTable));
        }
        [HttpGet]
        [Route("invoices")]
        public async Task<IActionResult> GetInvoices(
            string token,
            [ModelBinder(typeof(BitpayDateTimeOffsetModelBinder))]
            DateTimeOffset? dateStart = null,
            [ModelBinder(typeof(BitpayDateTimeOffsetModelBinder))]
            DateTimeOffset? dateEnd = null,
            string orderId = null,
            string itemCode = null,
            string status = null,
            int? limit = null,
            int? offset = null)
        {
            if (User.Identity?.AuthenticationType == Security.Bitpay.BitpayAuthenticationTypes.Anonymous)
                return Forbid(Security.Bitpay.BitpayAuthenticationTypes.Anonymous);
            if (dateEnd != null)
                dateEnd = dateEnd.Value + TimeSpan.FromDays(1); //Should include the end day

            var query = new InvoiceQuery()
            {
                Take = limit,
                Skip = offset,
                EndDate = dateEnd,
                StartDate = dateStart,
                OrderId = orderId == null ? null : new[] { orderId },
                ItemCode = itemCode == null ? null : new[] { itemCode },
                Status = status == null ? null : new[] { status },
                StoreId = new[] { this.HttpContext.GetStoreData().Id }
            };

            var entities = (await _InvoiceRepository.GetInvoices(query))
                            .Select((o) => o.EntityToDTO(_bitpayExtensions, Url, _currencyNameTable)).ToArray();

            return Json(DataWrapper.Create(entities));
        }

         internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(BitpayCreateInvoiceRequest invoice,
            StoreData store, string serverUrl, List<string> additionalTags = null,
            CancellationToken cancellationToken = default, Action<InvoiceEntity> entityManipulator = null)
        {
            var entity = await CreateInvoiceCoreRaw(invoice, store, serverUrl, additionalTags, cancellationToken, entityManipulator);
            var resp = entity.EntityToDTO(_bitpayExtensions, Url, _currencyNameTable);
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        internal async Task<InvoiceEntity> CreateInvoiceCoreRaw(BitpayCreateInvoiceRequest invoice, StoreData store, string serverUrl, List<string> additionalTags = null, CancellationToken cancellationToken = default, Action<InvoiceEntity> entityManipulator = null)
        {
            var storeBlob = store.GetStoreBlob();
            var entity = _InvoiceRepository.CreateNewInvoice(store.Id);
            entity.ExpirationTime = invoice.ExpirationTime is { } v ? v : entity.InvoiceTime + storeBlob.InvoiceExpiration;
            entity.MonitoringExpiration = entity.ExpirationTime + storeBlob.MonitoringExpiration;
            if (entity.ExpirationTime - TimeSpan.FromSeconds(30.0) < entity.InvoiceTime)
            {
                throw new BitpayHttpException(400, "The expirationTime is set too soon");
            }
            if (entity.Price < 0.0m)
            {
                throw new BitpayHttpException(400, "The price should be 0 or more.");
            }
            if (entity.Price > GreenfieldConstants.MaxAmount)
            {
                throw new BitpayHttpException(400, $"The price should less than {GreenfieldConstants.MaxAmount}.");
            }
            entity.Metadata.OrderId = invoice.OrderId;
            entity.Metadata.PosDataLegacy = invoice.PosData;
            entity.ServerUrl = serverUrl;
            entity.FullNotifications = invoice.FullNotifications || invoice.ExtendedNotifications;
            entity.ExtendedNotifications = invoice.ExtendedNotifications;
            entity.NotificationURLTemplate = invoice.NotificationURL;
            entity.NotificationEmail = invoice.NotificationEmail;
            if (additionalTags != null)
                entity.InternalTags.AddRange(additionalTags);
            FillBuyerInfo(invoice, entity);
            
            var price = invoice.Price;
            entity.Metadata.ItemCode = invoice.ItemCode;
            entity.Metadata.ItemDesc = invoice.ItemDesc;
            entity.Metadata.Physical = invoice.Physical;
            entity.Metadata.TaxIncluded = invoice.TaxIncluded;
            entity.Currency = invoice.Currency;
            if (price is { } vv)
            {
                entity.Price = vv;
                entity.Type = InvoiceType.Standard;
            }
            else
            {
                entity.Price = 0m;
                entity.Type = InvoiceType.TopUp;
            }

            entity.StoreSupportUrl = storeBlob.StoreSupportUrl;
            entity.RedirectURLTemplate = invoice.RedirectURL ?? store.StoreWebsite;
            entity.RedirectAutomatically =
                invoice.RedirectAutomatically.GetValueOrDefault(storeBlob.RedirectAutomatically);
            entity.SpeedPolicy = ParseSpeedPolicy(invoice.TransactionSpeed, store.SpeedPolicy);

            IPaymentFilter excludeFilter = null;
            if (invoice.PaymentCurrencies?.Any() is true)
            {
                invoice.SupportedTransactionCurrencies ??=
                    new Dictionary<string, InvoiceSupportedTransactionCurrency>();
                foreach (string paymentCurrency in invoice.PaymentCurrencies)
                {
                    invoice.SupportedTransactionCurrencies.TryAdd(paymentCurrency,
                        new InvoiceSupportedTransactionCurrency() { Enabled = true });
                }
            }
            if (invoice.SupportedTransactionCurrencies != null && invoice.SupportedTransactionCurrencies.Count != 0)
            {
                var supportedTransactionCurrencies = invoice.SupportedTransactionCurrencies
                                                            .Where(c => c.Value.Enabled)
                                                            .Select(c => PaymentMethodId.TryParse(c.Key, out var p) ? p : null)
                                                            .Where(c => c != null)
                                                            .ToHashSet();
                excludeFilter = PaymentFilter.Where(p => !supportedTransactionCurrencies.Contains(p));
            }
            entity.PaymentTolerance = storeBlob.PaymentTolerance;
            if (invoice.DefaultPaymentMethod is not null && PaymentMethodId.TryParse(invoice.DefaultPaymentMethod, out var defaultPaymentMethod))
            {
                entity.DefaultPaymentMethod = defaultPaymentMethod;
            }

            return await _InvoiceController.CreateInvoiceCoreRaw(entity, store, excludeFilter, null, cancellationToken, entityManipulator);
        }

        private void FillBuyerInfo(BitpayCreateInvoiceRequest req, InvoiceEntity invoiceEntity)
        {
            var buyerInformation = invoiceEntity.Metadata;
            buyerInformation.BuyerAddress1 = req.BuyerAddress1;
            buyerInformation.BuyerAddress2 = req.BuyerAddress2;
            buyerInformation.BuyerCity = req.BuyerCity;
            buyerInformation.BuyerCountry = req.BuyerCountry;
            buyerInformation.BuyerEmail = req.BuyerEmail;
            buyerInformation.BuyerName = req.BuyerName;
            buyerInformation.BuyerPhone = req.BuyerPhone;
            buyerInformation.BuyerState = req.BuyerState;
            buyerInformation.BuyerZip = req.BuyerZip;
            var buyer = req.Buyer;
            if (buyer == null)
                return;
            buyerInformation.BuyerAddress1 ??= buyer.Address1;
            buyerInformation.BuyerAddress2 ??= buyer.Address2;
            buyerInformation.BuyerCity ??= buyer.City;
            buyerInformation.BuyerCountry ??= buyer.country;
            buyerInformation.BuyerEmail ??= buyer.email;
            buyerInformation.BuyerName ??= buyer.Name;
            buyerInformation.BuyerPhone ??= buyer.phone;
            buyerInformation.BuyerState ??= buyer.State;
            buyerInformation.BuyerZip ??= buyer.zip;
        }
        private SpeedPolicy ParseSpeedPolicy(string transactionSpeed, SpeedPolicy defaultPolicy)
        {
            if (transactionSpeed == null)
                return defaultPolicy;
            var mappings = new Dictionary<string, SpeedPolicy>();
            mappings.Add("low", SpeedPolicy.LowSpeed);
            mappings.Add("low-medium", SpeedPolicy.LowMediumSpeed);
            mappings.Add("medium", SpeedPolicy.MediumSpeed);
            mappings.Add("high", SpeedPolicy.HighSpeed);
            if (!mappings.TryGetValue(transactionSpeed, out SpeedPolicy policy))
                policy = defaultPolicy;
            return policy;
        }
    }
}
