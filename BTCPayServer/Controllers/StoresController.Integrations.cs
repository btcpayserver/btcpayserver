using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Shopify;
using BTCPayServer.Services.Shopify.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [AllowAnonymous]
        [HttpGet("{storeId}/integrations/shopify/shopify.js")]
        public async Task<IActionResult> ShopifyJavascript(string storeId)
        {

            string[] fileList = new[]
            {
                "modal/btcpay.js", 
                "shopify/btcpay-browser-client.js",
                "shopify/btcpay-shopify-checkout.js"
            };
            if (_BtcpayServerOptions.BundleJsCss)
            {
                fileList = new[] {_bundleProvider.GetBundle("shopify-bundle.min.js").OutputFileUrl};
            }

            var jsFile = $"var BTCPAYSERVER_URL = \"{Request.GetAbsoluteRoot()}\"; var STORE_ID = \"{storeId}\";";
            foreach (var file in fileList)
            {
                await using var stream = _webHostEnvironment.WebRootFileProvider
                    .GetFileInfo(file).CreateReadStream();
                using var reader = new StreamReader(stream);
                jsFile += Environment.NewLine + await reader.ReadToEndAsync();
            }

            return Content(jsFile, "text/javascript");
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
                        ShopName = parsedUrl.Host.Replace(".myshopify.com", "", StringComparison.InvariantCultureIgnoreCase)
                    };
                    command = "ShopifySaveCredentials";

                }
                catch (Exception)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "The provided example url was invalid.";
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
                    catch
                    {
                        TempData[WellKnownTempData.ErrorMessage] =
                            "Shopify rejected provided credentials, please correct values and again";
                        return View("Integrations", vm);
                    }

                    shopify.CredentialsValid = true;

                    var blob = CurrentStore.GetStoreBlob();
                    blob.Shopify = shopify;
                    if (CurrentStore.SetStoreBlob(blob))
                    {
                        await _Repo.UpdateStore(CurrentStore);
                    }

                    TempData[WellKnownTempData.SuccessMessage] = "Shopify credentials successfully updated";
                    break;
                }
                case "ShopifyIntegrate":
                {
                    var blob = CurrentStore.GetStoreBlob();

                    var apiClient = new ShopifyApiClient(clientFactory, blob.Shopify.CreateShopifyApiCredentials());
                    var result = await apiClient.CreateScript(Url.Action("ShopifyJavascript", "Stores",
                        new {storeId = CurrentStore.Id}, Request.Scheme));

                    blob.Shopify.ScriptId = result.ScriptTag?.Id.ToString(CultureInfo.InvariantCulture);

                    blob.Shopify.IntegratedAt = DateTimeOffset.UtcNow;
                    if (CurrentStore.SetStoreBlob(blob))
                    {
                        await _Repo.UpdateStore(CurrentStore);
                    }

                    TempData[WellKnownTempData.SuccessMessage] = "Shopify integration successfully turned on";
                    break;
                }
                case "ShopifyClearCredentials":
                {
                    var blob = CurrentStore.GetStoreBlob();

                    if (blob.Shopify.IntegratedAt.HasValue)
                    {
                        if (!string.IsNullOrEmpty(blob.Shopify.ScriptId))
                        {
                            try
                            {
                                var apiClient = new ShopifyApiClient(clientFactory,
                                    blob.Shopify.CreateShopifyApiCredentials());
                                await apiClient.RemoveScript(blob.Shopify.ScriptId);
                            }
                            catch (Exception e)
                            {
                                //couldnt remove the script but that's ok
                            }
                        }
                    }

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
