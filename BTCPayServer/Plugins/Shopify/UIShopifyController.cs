using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Shopify.ApiModels;
using BTCPayServer.Plugins.Shopify.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Shopify
{

    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIShopifyController : Controller
    {
        private readonly BTCPayServerEnvironment _btcPayServerEnvironment;
        private readonly IOptions<BTCPayServerOptions> _btcPayServerOptions;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly StoreRepository _storeRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly UIInvoiceController _invoiceController;
        private readonly IJsonHelper _jsonHelper;
        private readonly IHttpClientFactory _clientFactory;

        public UIShopifyController(BTCPayServerEnvironment btcPayServerEnvironment,
            IOptions<BTCPayServerOptions> btcPayServerOptions,
            IWebHostEnvironment webHostEnvironment,
            StoreRepository storeRepository,
            InvoiceRepository invoiceRepository,
            UIInvoiceController invoiceController,
            IJsonHelper jsonHelper,
            IHttpClientFactory clientFactory)
        {
            _btcPayServerEnvironment = btcPayServerEnvironment;
            _btcPayServerOptions = btcPayServerOptions;
            _webHostEnvironment = webHostEnvironment;
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
            _invoiceController = invoiceController;
            _jsonHelper = jsonHelper;
            _clientFactory = clientFactory;
        }
        public StoreData CurrentStore
        {
            get
            {
                return this.HttpContext.GetStoreData();
            }
        }
        private static string _cachedShopifyJavascript;

        private async Task<string> GetJavascript()
        {
            if (!string.IsNullOrEmpty(_cachedShopifyJavascript) && !_btcPayServerEnvironment.IsDeveloping)
            {
                return _cachedShopifyJavascript;
            }

            string[] fileList = new[] { "modal/btcpay.js", "shopify/btcpay-shopify.js" };

            foreach (var file in fileList)
            {
                await using var stream = _webHostEnvironment.WebRootFileProvider
                    .GetFileInfo(file).CreateReadStream();
                using var reader = new StreamReader(stream);
                _cachedShopifyJavascript += Environment.NewLine + await reader.ReadToEndAsync();
            }

            return _cachedShopifyJavascript;
        }

        [AllowAnonymous]
        [HttpGet("stores/{storeId}/plugins/shopify/shopify.js")]
        public async Task<IActionResult> ShopifyJavascript(string storeId)
        {
            var jsFile =
                $"var BTCPAYSERVER_URL = {_jsonHelper.Serialize(Request.GetAbsoluteRoot())}; var STORE_ID = {_jsonHelper.Serialize(storeId)}; {await GetJavascript()}";
            return Content(jsFile, "text/javascript");
        }

        [RateLimitsFilter(ZoneLimits.Shopify, Scope = RateLimitsScope.RemoteAddress)]
        [AllowAnonymous]
        [EnableCors(CorsPolicies.All)]
        [HttpGet("stores/{storeId}/plugins/shopify/{orderId}")]
        public async Task<IActionResult> ShopifyInvoiceEndpoint(
            string storeId, string orderId, decimal amount, bool checkOnly = false)
        {
            var shopifySearchTerm = $"{ShopifyOrderMarkerHostedService.SHOPIFY_ORDER_ID_PREFIX}{orderId}";
            var matchedExistingInvoices = await _invoiceRepository.GetInvoices(new InvoiceQuery()
            {
                TextSearch = shopifySearchTerm,
                StoreId = new[] { storeId }
            });
            matchedExistingInvoices = matchedExistingInvoices.Where(entity =>
                    entity.GetInternalTags(ShopifyOrderMarkerHostedService.SHOPIFY_ORDER_ID_PREFIX)
                        .Any(s => s == orderId))
                .ToArray();

            var firstInvoiceStillPending =
                matchedExistingInvoices.FirstOrDefault(entity =>
                    entity.GetInvoiceState().Status == InvoiceStatusLegacy.New);
            if (firstInvoiceStillPending != null)
            {
                return Ok(new
                {
                    invoiceId = firstInvoiceStillPending.Id,
                    status = firstInvoiceStillPending.Status.ToString().ToLowerInvariant()
                });
            }

            var firstInvoiceSettled =
                matchedExistingInvoices.LastOrDefault(entity =>
                    new[] { InvoiceStatusLegacy.Paid, InvoiceStatusLegacy.Complete, InvoiceStatusLegacy.Confirmed }
                        .Contains(
                            entity.GetInvoiceState().Status));

            var store = await _storeRepository.FindStore(storeId);
            var shopify = store?.GetStoreBlob()?.GetShopifySettings();
            ShopifyApiClient client = null;
            ShopifyOrder order = null;
            if (shopify?.IntegratedAt.HasValue is true)
            {
                client = new ShopifyApiClient(_clientFactory, shopify.CreateShopifyApiCredentials());
                order = await client.GetOrder(orderId);
                if (order?.Id is null)
                {
                    return NotFound();
                }
            }

            if (firstInvoiceSettled != null)
            {
                //if BTCPay was shut down before the tx managed to get registered on shopify, this will fix it on the next UI load in shopify
                if (client != null && order?.FinancialStatus == "pending" &&
                    firstInvoiceSettled.Status != InvoiceStatusLegacy.Paid)
                {
                    await new OrderTransactionRegisterLogic(client).Process(orderId, firstInvoiceSettled.Id,
                        firstInvoiceSettled.Currency,
                        firstInvoiceSettled.Price.ToString(CultureInfo.InvariantCulture), true);
                    order = await client.GetOrder(orderId);
                }

                return Ok(new
                {
                    invoiceId = firstInvoiceSettled.Id,
                    status = firstInvoiceSettled.Status.ToString().ToLowerInvariant()
                });
            }

            if (checkOnly)
            {
                return Ok();
            }

            if (shopify?.IntegratedAt.HasValue is true)
            {
                if (order?.Id is null ||
                    !new[] { "pending", "partially_paid" }.Contains(order.FinancialStatus))
                {
                    return NotFound();
                }

                //we create the invoice at due amount provided from order page or full amount if due amount is bigger than order amount
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(
                    new CreateInvoiceRequest()
                    {
                        Amount = amount < order.TotalOutstanding ? amount : order.TotalOutstanding,
                        Currency = order.PresentmentCurrency,
                        Metadata = new JObject
                        {
                            ["orderId"] = order.OrderNumber,
                            ["shopifyOrderId"] = order.Id,
                            ["shopifyOrderNumber"] = order.OrderNumber
                        },
                        AdditionalSearchTerms = new[]
                        {
                            order.OrderNumber.ToString(CultureInfo.InvariantCulture),
                            order.Id.ToString(CultureInfo.InvariantCulture),
                            shopifySearchTerm
                        }
                    }, store,
                    Request.GetAbsoluteRoot(), new List<string>() { shopifySearchTerm });

                return Ok(new { invoiceId = invoice.Id, status = invoice.Status.ToString().ToLowerInvariant() });
            }

            return NotFound();
        }

        [HttpGet]
        [Route("stores/{storeId}/plugins/shopify")]
        public IActionResult EditShopify()
        {
            var blob = CurrentStore.GetStoreBlob();

            return View(blob.GetShopifySettings());
        }


        [HttpPost("stores/{storeId}/plugins/shopify")]
        public async Task<IActionResult> EditShopify(string storeId,
            ShopifySettings vm, string command = "")
        {
            switch (command)
            {
                case "ShopifySaveCredentials":
                    {
                        var shopify = vm;
                        var validCreds = shopify != null && shopify?.CredentialsPopulated() == true;
                        if (!validCreds)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = "Please provide valid Shopify credentials";
                            return View(vm);
                        }
                        var apiClient = new ShopifyApiClient(_clientFactory, shopify.CreateShopifyApiCredentials());
                        try
                        {
                            await apiClient.OrdersCount();
                        }
                        catch (ShopifyApiException err)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = err.Message;
                            return View(vm);
                        }

                        var scopesGranted = await apiClient.CheckScopes();
                        if (!scopesGranted.Contains("read_orders") || !scopesGranted.Contains("write_orders"))
                        {
                            TempData[WellKnownTempData.ErrorMessage] =
                                "Please grant the private app permissions for read_orders, write_orders";
                            return View(vm);
                        }

                        // everything ready, proceed with saving Shopify integration credentials
                        shopify.IntegratedAt = DateTimeOffset.Now;

                        var blob = CurrentStore.GetStoreBlob();
                        blob.SetShopifySettings(shopify);
                        if (CurrentStore.SetStoreBlob(blob))
                        {
                            await _storeRepository.UpdateStore(CurrentStore);
                        }

                        TempData[WellKnownTempData.SuccessMessage] = "Shopify plugin successfully updated";
                        break;
                    }
                case "ShopifyClearCredentials":
                    {
                        var blob = CurrentStore.GetStoreBlob();
                        blob.SetShopifySettings(null);
                        if (CurrentStore.SetStoreBlob(blob))
                        {
                            await _storeRepository.UpdateStore(CurrentStore);
                        }

                        TempData[WellKnownTempData.SuccessMessage] = "Shopify plugin credentials cleared";
                        break;
                    }
            }

            return RedirectToAction(nameof(EditShopify), new { storeId = CurrentStore.Id });
        }
    }

}
