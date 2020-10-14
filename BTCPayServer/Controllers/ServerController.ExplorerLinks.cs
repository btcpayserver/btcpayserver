using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers
{

    public class ExplorerLinksViewModel
    {
        public List<Item> Items { get; set; }

        public class Item
        {
            public string CryptoCode { get; set; }
            public string Name { get; set; }
            public string Link { get; set; }
            public string DefaultLink { get; set; }
        }
        
    }

    public class ExplorerLinkSettings
    {
        public Dictionary<string, string> NetworkLinks { get; set; }
    }
    public partial class ServerController
    {
        [HttpGet("server/network-links")]
        public IActionResult UpdateExplorerLinks(
            [FromServices] BTCPayNetworkProvider btcPayNetworkProvider)
        {
            return View(new ExplorerLinksViewModel()
            {
                Items = btcPayNetworkProvider.GetAll().Select(network => new ExplorerLinksViewModel.Item()
                {
                    Link = network.BlockExplorerLink,
                    Name = network.DisplayName,
                    CryptoCode = network.CryptoCode,
                    DefaultLink = network.BlockExplorerLinkDefault
                }).ToList()
            });
        }

        [HttpPost("server/network-links")]
        public async Task<IActionResult> UpdateExplorerLinks(
            [FromServices] BTCPayNetworkProvider btcPayNetworkProvider, ExplorerLinksViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            foreach (var item in vm.Items)
            {
                btcPayNetworkProvider.GetNetwork(item.CryptoCode).BlockExplorerLink = item.Link;
            }

            await _SettingsRepository.UpdateSetting(new ExplorerLinkSettings()
            {
                NetworkLinks = vm.Items.ToDictionary(item => item.CryptoCode, item => item.Link)
            });
            TempData.SetStatusMessageModel(new StatusMessageModel() {Message = "Updated network link"});
            return RedirectToAction("UpdateExplorerLinks");
        }

        public static async Task LoadNetworkLinkOverrides(SettingsRepository settingsRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            var settings = await settingsRepository.GetSettingAsync<ExplorerLinkSettings>();
            if (settings?.NetworkLinks?.Any() is true)
            {
                foreach (var item in settings.NetworkLinks)
                {
                    btcPayNetworkProvider.GetNetwork(item.Key).BlockExplorerLink = item.Value;
                }
            }
        }
    }
}
