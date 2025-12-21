using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class UIAppsController : Controller
    {
        public UIAppsController(
            UserManager<ApplicationUser> userManager,
            PaymentMethodHandlerDictionary handlers,
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepository,
            IFileService fileService,
            AppService appService,
            IStringLocalizer stringLocalizer,
            IHtmlHelper html)
        {
            _userManager = userManager;
            _handlers = handlers;
            _networkProvider = networkProvider;
            _storeRepository = storeRepository;
            _fileService = fileService;
            _appService = appService;
            Html = html;
            StringLocalizer = stringLocalizer;
        }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly StoreRepository _storeRepository;
        private readonly IFileService _fileService;
        private readonly AppService _appService;

        public string CreatedAppId { get; set; }
        public IHtmlHelper Html { get; }
        public IStringLocalizer StringLocalizer { get; }

        public class AppUpdated
        {
            public string AppId { get; set; }
            public object Settings { get; set; }
            public string StoreId { get; set; }
        }

        [HttpGet("/apps/{appId}")]
        public async Task<IActionResult> RedirectToApp(string appId)
        {
            var app = await _appService.GetApp(appId, null);
            if (app is null)
                return NotFound();

            var res = await _appService.ViewLink(app);
            if (res is null)
            {
                return NotFound();
            }

            return Redirect(res);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("/stores/{storeId}/apps")]
        public async Task<IActionResult> ListApps(
            string storeId,
            string sortOrder = null,
            string sortOrderColumn = null,
            bool archived = false
        )
        {
            var store = GetCurrentStore();
            var apps = (await _appService.GetAllApps(GetUserId(), false, store.Id, archived))
                .Where(app => app.Archived == archived);

            if (sortOrder != null && sortOrderColumn != null)
            {
                apps = apps.OrderByDescending(app =>
                {
                    return sortOrderColumn switch
                    {
                        nameof(app.AppName) => app.AppName,
                        nameof(app.StoreName) => app.StoreName,
                        nameof(app.AppType) => app.AppType,
                        _ => app.Id
                    };
                });

                switch (sortOrder)
                {
                    case "desc":
                        ViewData[$"{sortOrderColumn}NextSortOrder"] = "asc";
                        break;
                    case "asc":
                        apps = apps.Reverse();
                        ViewData[$"{sortOrderColumn}NextSortOrder"] = "desc";
                        break;
                }
            }

            return View(new ListAppsViewModel
            {
                Apps = apps.ToArray()
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("/stores/{storeId}/apps/create/{appType?}")]
        public IActionResult CreateApp(string storeId, string appType = null)
        {
            var vm = new CreateAppViewModel(_appService)
            {
                StoreId = storeId,
                AppType = appType,
                SelectedAppType = appType
            };
            return View(vm);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("/stores/{storeId}/apps/create/{appType?}")]
        public async Task<IActionResult> CreateApp(string storeId, CreateAppViewModel vm)
        {
            var store = GetCurrentStore();
            if (store == null)
            {
                return NotFound();
            }
            if (!store.AnyPaymentMethodAvailable(_handlers))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"To create a {vm.AppType} app, you need to <a href='{Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { cryptoCode = _networkProvider.DefaultNetwork.CryptoCode, storeId })}' class='alert-link'>set up a wallet</a> first",
                    AllowDismiss = false
                });
                return View(vm);
            }
            vm.StoreId = store.Id;
            var type = _appService.GetAppType(vm.AppType ?? vm.SelectedAppType);
            if (type is null)
            {
                ModelState.AddModelError(nameof(vm.SelectedAppType), StringLocalizer["Invalid App Type"]);
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var appData = new AppData
            {
                StoreDataId = store.Id,
                Name = vm.AppName,
                AppType = type!.Type
            };

            var defaultCurrency = await GetStoreDefaultCurrentIfEmpty(appData.StoreDataId, null);
            await _appService.SetDefaultSettings(appData, defaultCurrency);
            await _appService.UpdateOrCreateApp(appData);

            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["App successfully created"].Value;
            CreatedAppId = appData.Id;

            var url = await type.ConfigureLink(appData);
            return Redirect(url);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/delete")]
        public IActionResult DeleteApp(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            return View("Confirm", new ConfirmModel(StringLocalizer["Delete app"], $"The app <strong>{Html.Encode(app.Name)}</strong> and its settings will be permanently deleted. Are you sure?", StringLocalizer["Delete"]));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            if (await _appService.DeleteApp(app))
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["App deleted successfully."].Value;

            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = app.StoreDataId });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/archive")]
        public async Task<IActionResult> ToggleArchive(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            var type = _appService.GetAppType(app.AppType);
            if (type is null)
            {
                return UnprocessableEntity();
            }

            var archived = !app.Archived;
            if (await _appService.SetArchived(app, archived))
            {
                TempData[WellKnownTempData.SuccessMessage] = archived
                    ? StringLocalizer["The app has been archived and will no longer appear in the apps list by default."].Value
                    : StringLocalizer["The app has been unarchived and will appear in the apps list by default again."].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = archived
                    ? StringLocalizer["Failed to archive the app."].Value
                    : StringLocalizer["Failed to unarchive the app."].Value;
            }

            var url = await type.ConfigureLink(app);
            return Redirect(url);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/upload-file")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> FileUpload(IFormFile file)
        {
            var app = GetCurrentApp();
            var userId = GetUserId();
            if (app is null || userId is null)
                return NotFound();

            if (!file.FileName.IsValidFileName())
            {
                return Json(new { error = "Invalid file name" });
            }
            if (!file.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
            {
                return Json(new { error = "The file needs to be an image" });
            }
            if (file.Length > 500_000)
            {
                return Json(new { error = "The file size should be less than 0.5MB" });
            }
            var formFile = await file.Bufferize();
            if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
            {
                return Json(new { error = "The file needs to be an image" });
            }
            try
            {
                var storedFile = await _fileService.AddFile(file, userId);
                var fileId = storedFile.Id;
                var fileUrl = await _fileService.GetFileUrl(Request.GetAbsoluteRootUri(), fileId);
                return Json(new { fileId, fileUrl });
            }
            catch (Exception e)
            {
                return Json(new { error = $"Could not save file: {e.Message}" });
            }
        }

        async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                var store = await _storeRepository.FindStore(storeId);
                currency = store?.GetStoreBlob().DefaultCurrency;
            }
            return currency?.Trim().ToUpperInvariant();
        }

        private string GetUserId() => _userManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private AppData GetCurrentApp() => HttpContext.GetAppData();
    }
}
