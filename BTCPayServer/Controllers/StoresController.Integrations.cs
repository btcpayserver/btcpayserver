using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Shopify;
using BTCPayServer.Services.Shopify.ApiModels;
using BTCPayServer.Services.Shopify.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        private static string _cachedShopifyJavascript;

        private async Task<string> GetJavascript()
        {
            if (!string.IsNullOrEmpty(_cachedShopifyJavascript) && !_BTCPayEnv.IsDeveloping)
            {
                return _cachedShopifyJavascript;
            }

            string[] fileList = _BtcpayServerOptions.BundleJsCss
                ? new[] {"bundles/shopify-bundle.min.js"}
                : new[] {"modal/btcpay.js", "shopify/btcpay-shopify.js"};


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
        [HttpGet("{storeId}/integrations/shopify/shopify.js")]
        public async Task<IActionResult> ShopifyJavascript(string storeId)
        {
            var jsFile =
                $"var BTCPAYSERVER_URL = \"{Request.GetAbsoluteRoot()}\"; var STORE_ID = \"{storeId}\"; {await GetJavascript()}";
            return Content(jsFile, "text/javascript");
        }

        [RateLimitsFilter(ZoneLimits.Shopify, Scope = RateLimitsScope.RemoteAddress)]
        [AllowAnonymous]
        [EnableCors(CorsPolicies.All)]
        [HttpGet("{storeId}/integrations/shopify/{orderId}")]
        public async Task<IActionResult> ShopifyInvoiceEndpoint(
            [FromServices] InvoiceRepository invoiceRepository,
            [FromServices] InvoiceController invoiceController,
            [FromServices] IHttpClientFactory httpClientFactory,
            string storeId, string orderId, decimal amount, bool checkOnly = false)
        {
            var invoiceOrderId = $"{ShopifyOrderMarkerHostedService.SHOPIFY_ORDER_ID_PREFIX}{orderId}";
            var matchedExistingInvoices = await invoiceRepository.GetInvoices(new InvoiceQuery()
            {
                OrderId = new[] {invoiceOrderId}, StoreId = new[] {storeId}
            });
            matchedExistingInvoices = matchedExistingInvoices.Where(entity =>
                    entity.GetInternalTags(ShopifyOrderMarkerHostedService.SHOPIFY_ORDER_ID_PREFIX)
                        .Any(s => s == orderId))
                .ToArray();

            var firstInvoiceStillPending =
                matchedExistingInvoices.FirstOrDefault(entity => entity.GetInvoiceState().Status == InvoiceStatus.New);
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
                    new[] {InvoiceStatus.Paid, InvoiceStatus.Complete, InvoiceStatus.Confirmed}.Contains(
                        entity.GetInvoiceState().Status));

            var store = await _Repo.FindStore(storeId);
            var shopify = store?.GetStoreBlob()?.Shopify;
            ShopifyApiClient client = null;
            ShopifyOrder order = null;
            if (shopify?.IntegratedAt.HasValue is true)
            {
                client = new ShopifyApiClient(httpClientFactory, shopify.CreateShopifyApiCredentials());
                order = await client.GetOrder(orderId);
                if (string.IsNullOrEmpty(order?.Id))
                {
                    return NotFound();
                }
            }

            if (firstInvoiceSettled != null)
            {
                //if BTCPay was shut down before the tx managed to get registered on shopify, this will fix it on the next UI load in shopify
                if (client != null && order?.FinancialStatus == "pending" &&
                    firstInvoiceSettled.Status != InvoiceStatus.Paid)
                {
                    await new OrderTransactionRegisterLogic(client).Process(orderId, firstInvoiceSettled.Id,
                        firstInvoiceSettled.Currency,
                        firstInvoiceSettled.Price.ToString(CultureInfo.InvariantCulture), true);
                    order = await client.GetOrder(orderId);
                }

                if (order?.FinancialStatus != "pending" && order?.FinancialStatus != "partially_paid")
                {
                    return Ok(new
                    {
                        invoiceId = firstInvoiceSettled.Id,
                        status = firstInvoiceSettled.Status.ToString().ToLowerInvariant()
                    });
                }
            }

            if (checkOnly)
            {
                return Ok();
            }

            if (shopify?.IntegratedAt.HasValue is true)
            {
                if (string.IsNullOrEmpty(order?.Id) ||
                    !new[] {"pending", "partially_paid"}.Contains(order.FinancialStatus))
                {
                    return NotFound();
                }

                //we create the invoice at due amount provided from order page or full amount if due amount is bigger than order amount
                var invoice = await invoiceController.CreateInvoiceCoreRaw(
                    new CreateInvoiceRequest()
                    {
                        Amount = amount < order.TotalPrice ? amount : order.TotalPrice,
                        Currency = order.Currency,
                        Metadata = new JObject {["orderId"] = invoiceOrderId}
                    }, store,
                    Request.GetAbsoluteUri(""), new List<string>() {invoiceOrderId});

                return Ok(new {invoiceId = invoice.Id, status = invoice.Status.ToString().ToLowerInvariant()});
            }

            return NotFound();
        }


        [HttpGet]
        [Route("{storeId}/integrations")]
        [Route("{storeId}/integrations/shopify")]
        public IActionResult Integrations()
        {
            var blob = CurrentStore.GetStoreBlob();

            var vm = new IntegrationsViewModel {Shopify = blob.Shopify};

            return View("Integrations", vm);
        }

        [HttpPost]
        [Route("{storeId}/integrations/shopify")]
        public async Task<IActionResult> Integrations([FromServices] IHttpClientFactory clientFactory,
            IntegrationsViewModel vm, string command = "", string exampleUrl = "")
        {
            if (!string.IsNullOrEmpty(exampleUrl))
            {
                try
                {
                    //https://{apikey}:{password}@{hostname}/admin/api/{version}/{resource}.json
                    var parsedUrl = new Uri(exampleUrl);
                    var userInfo = parsedUrl.UserInfo.Split(":");
                    vm.Shopify = new ShopifySettings()
                    {
                        ApiKey = userInfo[0],
                        Password = userInfo[1],
                        ShopName = parsedUrl.Host.Replace(".myshopify.com", "",
                            StringComparison.InvariantCultureIgnoreCase)
                    };
                    command = "ShopifySaveCredentials";
                }
                catch (Exception)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "The provided Example Url was invalid.";
                    return View("Integrations", vm);
                }
            }

            switch (command)
            {
                case "ShopifySaveCredentials":
                {
                    var shopify = vm.Shopify;
                    var validCreds = shopify != null && shopify?.CredentialsPopulated() == true;
                    if (!validCreds)
                    {
                        TempData[WellKnownTempData.ErrorMessage] = "Please provide valid Shopify credentials";
                        return View("Integrations", vm);
                    }

                    var apiClient = new ShopifyApiClient(clientFactory, shopify.CreateShopifyApiCredentials());
                    try
                    {
                        await apiClient.OrdersCount();
                    }
                    catch (ShopifyApiException)
                    {
                        TempData[WellKnownTempData.ErrorMessage] =
                            "Shopify rejected provided credentials, please correct values and try again";
                        return View("Integrations", vm);
                    }

                    var scopesGranted = await apiClient.CheckScopes();
                    if (!scopesGranted.Contains("read_orders") || !scopesGranted.Contains("write_orders"))
                    {
                        TempData[WellKnownTempData.ErrorMessage] =
                            "Please grant the private app permissions for read_orders, write_orders";
                        return View("Integrations", vm);
                    }

                    // everything ready, proceed with saving Shopify integration credentials
                    shopify.IntegratedAt = DateTimeOffset.Now;

                    var blob = CurrentStore.GetStoreBlob();
                    blob.Shopify = shopify;
                    if (CurrentStore.SetStoreBlob(blob))
                    {
                        await _Repo.UpdateStore(CurrentStore);
                    }

                    TempData[WellKnownTempData.SuccessMessage] = "Shopify integration successfully updated";
                    break;
                }
                case "ShopifyClearCredentials":
                {
                    var blob = CurrentStore.GetStoreBlob();
                    blob.Shopify = null;
                    if (CurrentStore.SetStoreBlob(blob))
                    {
                        await _Repo.UpdateStore(CurrentStore);
                    }

                    TempData[WellKnownTempData.SuccessMessage] = "Shopify integration credentials cleared";
                    break;
                }
            }

            return RedirectToAction(nameof(Integrations), new {storeId = CurrentStore.Id});
        }
    }
}
