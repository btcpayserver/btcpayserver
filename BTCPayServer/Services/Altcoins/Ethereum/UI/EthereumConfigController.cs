#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SettingsRepository _settingsRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;

        public EthereumConfigController(IHttpClientFactory httpClientFactory, SettingsRepository settingsRepository,
            UserManager<ApplicationUser> userManager,
            EventAggregator eventAggregator)
        {
            _httpClientFactory = httpClientFactory;
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
            if (current?.Web3ProviderUrl != vm.Web3ProviderUrl)
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
    }
}
#endif
