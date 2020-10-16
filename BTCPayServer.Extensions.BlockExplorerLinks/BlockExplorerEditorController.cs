using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Extensions.BlockExplorerLinks
{
    [Authorize(Policy = BTCPayServer.Client.Policies.CanModifyServerSettings,
        AuthenticationSchemes = BTCPayServer.Security.AuthenticationSchemes.Cookie)]
    public class BlockExplorerEditorController : Controller
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly ISettingsRepository _settingsRepository;

        public BlockExplorerEditorController(BTCPayNetworkProvider btcPayNetworkProvider,
            ISettingsRepository settingsRepository)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
        }

        [HttpGet("server/network-links")]
        public IActionResult UpdateExplorerLinks()
        {
            return View(new ExplorerLinksViewModel()
            {
                Items = _btcPayNetworkProvider.GetAll().Select(network => new ExplorerLinksViewModel.Item()
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

            await _settingsRepository.UpdateSetting(new ExplorerLinkSettings()
            {
                NetworkLinks = vm.Items.ToDictionary(item => item.CryptoCode, item => item.Link)
            });
            TempData.SetStatusMessageModel(new StatusMessageModel() {Message = "Updated network link"});
            return RedirectToAction("UpdateExplorerLinks");
        }
    }
}
