using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class AppsController : Controller
    {
        public AppsController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContextFactory contextFactory,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            AppsHelper appsHelper)
        {
            _UserManager = userManager;
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _AppsHelper = appsHelper;
        }

        private UserManager<ApplicationUser> _UserManager;
        private ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _EventAggregator;
        private BTCPayNetworkProvider _NetworkProvider;
        private AppsHelper _AppsHelper;

        [TempData]
        public string StatusMessage { get; set; }
        public string CreatedAppId { get; set; }

        public async Task<IActionResult> ListApps()
        {
            var apps = await _AppsHelper.GetAllApps(GetUserId());
            return View(new ListAppsViewModel()
            {
                Apps = apps
            });
        }

        [HttpPost]
        [Route("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            if (await _AppsHelper.DeleteApp(appData))
                StatusMessage = "App removed successfully";
            return RedirectToAction(nameof(ListApps));
        }

        [HttpGet]
        [Route("create")]
        public async Task<IActionResult> CreateApp()
        {
            var stores = await _AppsHelper.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                StatusMessage = new StatusMessageModel()
                {
                    Html =
                        $"Error: You must have created at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                }.ToString();
                return RedirectToAction(nameof(ListApps));
            }
            var vm = new CreateAppViewModel();
            vm.SetStores(stores);
            return View(vm);
        }

        [HttpPost]
        [Route("create")]
        public async Task<IActionResult> CreateApp(CreateAppViewModel vm)
        {
            var stores = await _AppsHelper.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                StatusMessage = new StatusMessageModel()
                {
                    Html =
                        $"Error: You must have created at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                }.ToString();
                return RedirectToAction(nameof(ListApps));
            }
            var selectedStore = vm.SelectedStore;
            vm.SetStores(stores);
            vm.SelectedStore = selectedStore;

            if (!Enum.TryParse<AppType>(vm.SelectedAppType, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedAppType), "Invalid App Type");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            if (!stores.Any(s => s.Id == selectedStore))
            {
                StatusMessage = "Error: You are not owner of this store";
                return RedirectToAction(nameof(ListApps));
            }
            var id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20));
            using (var ctx = _ContextFactory.CreateContext())
            {
                var appData = new AppData() { Id = id };
                appData.StoreDataId = selectedStore;
                appData.Name = vm.Name;
                appData.AppType = appType.ToString();
                ctx.Apps.Add(appData);
                await ctx.SaveChangesAsync();
            }
            StatusMessage = "App successfully created";
            CreatedAppId = id;

            switch (appType)
            {
                case AppType.PointOfSale:
                    return RedirectToAction(nameof(UpdatePointOfSale), new { appId = id });
                case AppType.Crowdfund:
                    return RedirectToAction(nameof(UpdateCrowdfund), new { appId = id });
                default:
                    return RedirectToAction(nameof(ListApps));
            }
                
        }

        [HttpGet]
        [Route("{appId}/delete")]
        public async Task<IActionResult> DeleteApp(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = $"Delete app {appData.Name} ({appData.AppType})",
                Description = "This app will be removed from this store",
                Action = "Delete"
            });
        }

        private Task<AppData> GetOwnedApp(string appId, AppType? type = null)
        {
            return _AppsHelper.GetAppDataIfOwner(GetUserId(), appId, type);
        }

        
        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }
}
