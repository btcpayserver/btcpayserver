using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using Ganss.XSS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class AppsController : Controller
    {
        public AppsController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContextFactory contextFactory,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            CurrencyNameTable currencies,
            EmailSenderFactory emailSenderFactory,
            AppService AppService)
        {
            _UserManager = userManager;
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _currencies = currencies;
            _emailSenderFactory = emailSenderFactory;
            _AppService = AppService;
        }

        private UserManager<ApplicationUser> _UserManager;
        private ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _EventAggregator;
        private BTCPayNetworkProvider _NetworkProvider;
        private readonly CurrencyNameTable _currencies;
        private readonly EmailSenderFactory _emailSenderFactory;
        private AppService _AppService;

        public string CreatedAppId { get; set; }

        public async Task<IActionResult> ListApps()
        {
            var apps = await _AppService.GetAllApps(GetUserId());
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
            if (await _AppService.DeleteApp(appData))
                TempData[WellKnownTempData.SuccessMessage] = "App removed successfully";
            return RedirectToAction(nameof(ListApps));
        }

        [HttpGet]
        [Route("create")]
        public async Task<IActionResult> CreateApp()
        {
            var stores = await _AppService.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html =
                        $"Error: You need to create at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
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
            var stores = await _AppService.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html =
                        $"Error: You need to create at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
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
                TempData[WellKnownTempData.ErrorMessage] = "You are not owner of this store";
                return RedirectToAction(nameof(ListApps));
            }
            var appData = new AppData
            {
                StoreDataId = selectedStore, 
                Name = vm.Name, 
                AppType = appType.ToString()
            };
            await _AppService.UpdateOrCreateApp(appData);
            TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            CreatedAppId = appData.Id;

            switch (appType)
            {
                case AppType.PointOfSale:
                    return RedirectToAction(nameof(UpdatePointOfSale), new { appId = appData.Id });
                case AppType.Crowdfund:
                    return RedirectToAction(nameof(UpdateCrowdfund), new { appId = appData.Id });
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
            return _AppService.GetAppDataIfOwner(GetUserId(), appId, type);
        }

        
        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }

        private async Task<bool> IsEmailConfigured(string storeId)
        {
            return (await (_emailSenderFactory.GetEmailSender(storeId) as EmailSender)?.GetEmailSettings())?.IsComplete() is true;
        }
    }
}
