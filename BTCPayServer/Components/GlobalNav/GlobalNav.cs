using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Components.MainNav;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.GlobalNav
{
    public class GlobalNav : ViewComponent
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UriResolver _uriResolver;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerOptions _btcPayServerOptions;

        public GlobalNav(
            UserManager<ApplicationUser> userManager,
            UriResolver uriResolver,
            SettingsRepository settingsRepository,
            BTCPayServerOptions btcPayServerOptions)
        {
            _userManager = userManager;
            _uriResolver = uriResolver;
            _settingsRepository = settingsRepository;
            _btcPayServerOptions = btcPayServerOptions;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var serverSettings = await _settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var vm = new GlobalNavViewModel
            {
                ContactUrl = serverSettings.ContactUrl,
                DockerDeployment = _btcPayServerOptions.DockerDeployment,
                CurrentStoreId = store?.Id,
                MainNav = new MainNavViewModel
                {
                    Store = store
                }
            };

            var user = await _userManager.GetUserAsync(HttpContext.User);
            if (user != null)
            {
                var blob = user.GetBlob();
                var imageUrl = blob?.ImageUrl;
                vm.UserName = blob?.Name;
                vm.UserImageUrl = string.IsNullOrEmpty(imageUrl)
                    ? null
                    : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(imageUrl));
            }

            return View(vm);
        }
    }
}
