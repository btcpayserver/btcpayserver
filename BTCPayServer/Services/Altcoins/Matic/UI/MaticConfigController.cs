#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Matic.Configuration;
using BTCPayServer.Services.Altcoins.Matic.Filters;
using BTCPayServer.Services.Altcoins.Matic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Nethereum.Hex.HexConvertors.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Matic.UI
{
    [Route("maticconfig")]
    [OnlyIfSupportMatic]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class MaticConfigController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SettingsRepository _settingsRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;

        public MaticConfigController(IHttpClientFactory httpClientFactory, SettingsRepository settingsRepository,
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
            return View("Matic/UpdateChainConfig",
                (await _settingsRepository.GetSettingAsync<MaticLikeConfiguration>(
                    MaticLikeConfiguration.SettingsKey(chainId))) ?? new MaticLikeConfiguration()
                {
                    ChainId = chainId, Web3ProviderUrl = ""
                });
        }

        [HttpGet("{chainId}/cb")]
        public IActionResult Callback(int chainId)
        {
            _eventAggregator.Publish(new MaticService.CheckWatchers());
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "If the invoice was paid successfully and confirmed, the system will be enabled momentarily"
            });
            return RedirectToAction("UpdateChainConfig", new {chainId});
        }

        [HttpPost("{chainId}")]
        public async Task<IActionResult> UpdateChainConfig(int chainId, MaticLikeConfiguration vm)
        {
            var current = await _settingsRepository.GetSettingAsync<MaticLikeConfiguration>(
                MaticLikeConfiguration.SettingsKey(chainId));
            if (current?.Web3ProviderUrl != vm.Web3ProviderUrl)
            {
                vm.ChainId = chainId;
                await _settingsRepository.UpdateSetting(vm, MaticLikeConfiguration.SettingsKey(chainId));
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
