using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class AppsController : Controller
    {
        public AppsController(
            UserManager<ApplicationUser> userManager,
            EventAggregator eventAggregator,
            CurrencyNameTable currencies,
            EmailSenderFactory emailSenderFactory,
            AppService appService)
        {
            _userManager = userManager;
            _eventAggregator = eventAggregator;
            _currencies = currencies;
            _emailSenderFactory = emailSenderFactory;
            _appService = appService;
        }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;
        private readonly CurrencyNameTable _currencies;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly AppService _appService;

        public string CreatedAppId { get; private set; }

        public async Task<IActionResult> ListApps()
        {
            var apps = await _appService.GetAllApps(GetUserId());
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
            if (await _appService.DeleteApp(appData))
                TempData[WellKnownTempData.SuccessMessage] = "App removed successfully";
            return RedirectToAction(nameof(ListApps));
        }

        [HttpGet]
        [Route("create")]
        public async Task<IActionResult> CreateApp()
        {
            var stores = await _appService.GetOwnedStores(GetUserId());
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
            var stores = await _appService.GetOwnedStores(GetUserId());
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

            if (!Enum.TryParse(vm.SelectedAppType, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedAppType), "Invalid App Type");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            if (stores.All(s => s.Id != selectedStore))
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
            await _appService.UpdateOrCreateApp(appData);
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
            return _appService.GetAppDataIfOwner(GetUserId(), appId, type);
        }

        
        private string GetUserId()
        {
            return _userManager.GetUserId(User);
        }

        private async Task<bool> IsEmailConfigured(string storeId)
        {
            if (!(_emailSenderFactory.GetEmailSender(storeId) is EmailSender emailSender))
            {
                return false;
            }

            var emailSettings = await emailSender.GetEmailSettings();
            if (emailSettings == null)
            {
                return false;
            }

            return emailSettings.IsComplete() is true;
        }
    }
}
