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
    public class GlobalNav(
        UserManager<ApplicationUser> userManager,
        UriResolver uriResolver,
        SettingsRepository settingsRepository,
        BTCPayServerOptions btcPayServerOptions)
        : ViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var store = ViewContext.HttpContext.GetStoreDataOrNull();
            var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var vm = new GlobalNavViewModel
            {
                ContactUrl = serverSettings.ContactUrl,
                DockerDeployment = btcPayServerOptions.DockerDeployment,
                CurrentStoreId = store?.Id,
                MainNav = new MainNavViewModel
                {
                    Store = store
                }
            };

            var user = await userManager.GetUserAsync(HttpContext.User);
            if (user != null)
            {
                var blob = user.GetBlob();
                var imageUrl = blob?.ImageUrl;
                vm.UserName = blob?.Name;
                vm.UserImageUrl = string.IsNullOrEmpty(imageUrl)
                    ? null
                    : await uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(imageUrl));
            }

            return View(vm);
        }
    }
}
