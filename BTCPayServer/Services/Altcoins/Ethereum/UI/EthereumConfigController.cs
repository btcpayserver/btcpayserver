#if ALTCOINS_RELEASE || DEBUG
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Ethereum.Configuration;
using BTCPayServer.Services.Altcoins.Ethereum.Filters;
using BTCPayServer.Services.Altcoins.Ethereum.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Nethereum.Hex.HexConvertors.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Ethereum.UI
{
    [Route("ethconfig")]
    [OnlyIfSupportEth]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class EthereumConfigController : Controller
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;

        public EthereumConfigController(SettingsRepository settingsRepository, UserManager<ApplicationUser> userManager,
            EventAggregator eventAggregator)
        {
            _settingsRepository = settingsRepository;
            _userManager = userManager;
            _eventAggregator = eventAggregator;
        }

        [HttpGet("{chainId}")]
        public async Task<IActionResult> UpdateChainConfig(int chainId)
        {
            return View("Ethereum/UpdateChainConfig",
                (await _settingsRepository.GetSettingAsync<EthereumLikeConfiguration>(
                    EthereumLikeConfiguration.SettingsKey(chainId))) ?? new EthereumLikeConfiguration()
                {
                    ChainId = chainId, Web3ProviderUrl = ""
                });
        }

        [HttpGet("{chainId}/cb")]
        public IActionResult Callback(int chainId)
        {
            _eventAggregator.Publish(new EthereumService.CheckWatchers());
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "If the invoice was paid successfully and confirmed, the system will be enabled momentarily"
            });
            return RedirectToAction("UpdateChainConfig", new {chainId});
        }

        [HttpPost("{chainId}")]
        public async Task<IActionResult> UpdateChainConfig(int chainId, EthereumLikeConfiguration vm)
        {
            var current = await _settingsRepository.GetSettingAsync<EthereumLikeConfiguration>(
                EthereumLikeConfiguration.SettingsKey(chainId));
            if (current?.Web3ProviderUrl != vm.Web3ProviderUrl || current?.InvoiceId != vm.InvoiceId)
            {
                vm.ChainId = chainId;
                await _settingsRepository.UpdateSetting(vm, EthereumLikeConfiguration.SettingsKey(chainId));
            }

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success, Message = $"Chain {chainId} updated"
            });
            return RedirectToAction(nameof(UpdateChainConfig));
        }

        [HttpGet("{chainId}/p")]
        [HttpPost("{chainId}/p")]
        public async Task<IActionResult> CreateInvoice(int chainId)
        {
            var current = await _settingsRepository.GetSettingAsync<EthereumLikeConfiguration>(
                EthereumLikeConfiguration.SettingsKey(chainId));
            current ??= new EthereumLikeConfiguration() {ChainId = chainId};
            if (!string.IsNullOrEmpty(current?.InvoiceId) &&
                Request.Method.Equals("get", StringComparison.InvariantCultureIgnoreCase))
            {
                return View("Confirm",
                    new ConfirmModel()
                    {
                        Title = $"Generate new donation link?",
                        Description =
                            "This previously linked donation instructions will be erased. If you paid anything to it, you will lose access.",
                        Action = "Confirm and generate",
                    });
            }

            var user = await _userManager.GetUserAsync(User);

            HttpClient httpClient = new HttpClient(new HttpClientHandler() {AllowAutoRedirect = false});

            string invoiceUrl;
            var response = await httpClient.PostAsync($"{Server.HexToUTF8String()}{invoiceEndpoint.HexToUTF8String()}",
                new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("choiceKey", $"license_{chainId}"),
                    new KeyValuePair<string, string>("posData",
                        JsonConvert.SerializeObject(new {Host = Request.Host, ChainId = chainId})),
                    new KeyValuePair<string, string>("orderID", $"eth_{Request.Host}_{chainId}"),
                    new KeyValuePair<string, string>("email", user.Email),
                    new KeyValuePair<string, string>("redirectUrl",
                        Url.Action("Callback", "EthereumConfig", new {chainId}, Request.Scheme)),
                }));
            if (response.StatusCode == System.Net.HttpStatusCode.Found)
            {
                HttpResponseHeaders headers = response.Headers;
                if (headers != null && headers.Location != null)
                {
                    invoiceUrl = $"{Server.HexToUTF8String()}{headers.Location}";
                    current.InvoiceId = headers.Location.ToString()
                        .Replace("/i/", string.Empty, StringComparison.InvariantCultureIgnoreCase);
                    await UpdateChainConfig(chainId, current);
                    return Redirect(invoiceUrl);
                }
            }

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error, Message = $"Couldn't connect to donation server, try again later."
            });
            return RedirectToAction("UpdateChainConfig", new { chainId});
        }

        private string invoiceEndpoint = "0x2f617070732f3262706f754e74576b4b3543636e426d374833456a3346505a756f412f706f73";
        private static string Server = "0x68747470733a2f2f787061797365727665722e636f6d";
        public static NetworkType InvoiceEnforced = NetworkType.Mainnet;

        public static async Task<bool> CheckValid(NetworkType networkType, string  invoiceId)
        {
            if (networkType != InvoiceEnforced)
            {
                return true;
            }
            if (string.IsNullOrEmpty(invoiceId))
            {
                return false;
            }
            HttpClient httpClient = new HttpClient();
            var url = $"{Server.HexToUTF8String()}/i/{invoiceId}/status";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            var raw = await response.Content.ReadAsStringAsync();
            var status = JObject.Parse(raw)["status"].ToString();
            return  (status.Equals("complete", StringComparison.InvariantCultureIgnoreCase) ||
                     status.Equals("confirmed", StringComparison.InvariantCultureIgnoreCase));
            ;
        }
    }
}
#endif
