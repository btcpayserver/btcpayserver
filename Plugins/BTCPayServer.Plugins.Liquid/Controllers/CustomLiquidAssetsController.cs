using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Liquid.Models;
using BTCPayServer.Plugins.Liquid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Liquid.Controllers
{
    [Route("plugins/liquid/admin-settings")]
    [Authorize(Policy = BTCPayServer.Client.Policies.CanModifyServerSettings,
        AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class CustomLiquidAssetsController : Controller
    {
        private readonly CustomLiquidAssetsRepository _liquidAssetsRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public CustomLiquidAssetsController(CustomLiquidAssetsRepository liquidAssetsRepository,
            IHttpClientFactory httpClientFactory)
        {
            _liquidAssetsRepository = liquidAssetsRepository;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("")]
        public IActionResult Assets()
        {
            return View(new CustomLiquidAssetsViewModel()
            {
                Items = (_liquidAssetsRepository.Get()).Items,
                PendingChanges = _liquidAssetsRepository.ChangesPending
            });
        }

        [HttpPost("")]
        public async Task<IActionResult> Assets(CustomLiquidAssetsViewModel model, string command = null,
            string import = null)
        {
            if (command == "add")
            {
                ModelState.Clear();
                model.Items.Add(new CustomLiquidAssetsSettings.LiquidAssetConfiguration());
                return View(model);
            }

            if (command?.StartsWith("remove", StringComparison.InvariantCultureIgnoreCase) is true)
            {
                ModelState.Clear();
                var index = int.Parse(
                    command.Substring(command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1),
                    CultureInfo.InvariantCulture);
                model.Items.RemoveAt(index);
                return View(model);
            }

            if (import != null)
            {
                try
                {
                    if (!uint256.TryParse(import, out var _))
                    {
                        TempData["ErrorMessage"] =
                            "Asset Id to import was invalid.";
                        return View(model);
                    }

                    var data = JObject.Parse(await _httpClientFactory.CreateClient()
                        .GetStringAsync($"https://blockstream.info/liquid/api/asset/{import}"));

                    model.Items.Add(new CustomLiquidAssetsSettings.LiquidAssetConfiguration()
                    {
                        DisplayName = data["name"].Value<string>(),
                        Divisibility = data["precision"].Value<int>(),
                        AssetId = data["asset_id"].Value<string>(),
                        CryptoCode = data["ticker"].Value<string>().Replace("-", "").Replace("_", "")
                    });
                }
                catch (Exception)
                {
                    TempData["ErrorMessage"] =
                        "Asset Id to import was invalid.";
                    return View(model);
                }
            }

            for (int i = 0; i < model.Items.Count; i++)
            {
                if (!string.IsNullOrEmpty(model.Items[i].AssetId) &&
                    !uint256.TryParse(model.Items[i].AssetId, out var x))
                {
                    var inputName =
                        string.Format(CultureInfo.InvariantCulture, "Items[{0}].",
                            i.ToString(CultureInfo.InvariantCulture)) +
                        nameof(CustomLiquidAssetsSettings.LiquidAssetConfiguration.AssetId);

                    ModelState.AddModelError(inputName, "Invalid asset id format");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await _liquidAssetsRepository.Set(model);
            return RedirectToAction(nameof(Assets));
        }
    }
}
