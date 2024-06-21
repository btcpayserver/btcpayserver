#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using CrowdfundResetEvery = BTCPayServer.Client.Models.CrowdfundResetEvery;
using PosViewType = BTCPayServer.Client.Models.PosViewType;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldAppsController : ControllerBase
    {
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepository;
        private readonly CurrencyNameTable _currencies;
        private readonly UserManager<ApplicationUser> _userManager;

        public GreenfieldAppsController(
            AppService appService,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            CurrencyNameTable currencies,
            UserManager<ApplicationUser> userManager
        )
        {
            _appService = appService;
            _storeRepository = storeRepository;
            _currencies = currencies;
            _userManager = userManager;
        }

        [HttpPost("~/api/v1/stores/{storeId}/apps/crowdfund")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCrowdfundApp(string storeId, CrowdfundAppRequest request)
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return this.CreateAPIError(404, "store-not-found", "The store was not found");

            // This is not obvious, but we must have a non-null currency or else request validation may not work correctly
            request.TargetCurrency ??= store.GetStoreBlob().DefaultCurrency;

            ValidateAppRequest(request);
            ValidateCrowdfundAppRequest(request);
            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var appData = new AppData
            {
                StoreDataId = storeId,
                Name = request.AppName,
                AppType = CrowdfundAppType.AppType,
                Archived = request.Archived ?? false
            };

            var settings = ToCrowdfundSettings(request, new CrowdfundSettings { Title = request.Title ?? request.AppName });
            appData.SetSettings(settings);

            await _appService.UpdateOrCreateApp(appData);

            return Ok(ToCrowdfundModel(appData));
        }

        [HttpPost("~/api/v1/stores/{storeId}/apps/pos")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePointOfSaleApp(string storeId, PointOfSaleAppRequest request)
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return this.CreateAPIError(404, "store-not-found", "The store was not found");

            // This is not obvious, but we must have a non-null currency or else request validation may not work correctly
            request.Currency ??= store.GetStoreBlob().DefaultCurrency;

            ValidateAppRequest(request);
            ValidatePOSAppRequest(request);
            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var appData = new AppData
            {
                StoreDataId = storeId,
                Name = request.AppName,
                AppType = PointOfSaleAppType.AppType,
                Archived = request.Archived ?? false
            };

            var settings = ToPointOfSaleSettings(request, new PointOfSaleSettings { Title = request.Title ?? request.AppName });
            appData.SetSettings(settings);

            await _appService.UpdateOrCreateApp(appData);

            return Ok(ToPointOfSaleModel(appData));
        }

        [HttpPut("~/api/v1/apps/pos/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdatePointOfSaleApp(string appId, PointOfSaleAppRequest request)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType, includeArchived: true);
            if (app == null)
            {
                return AppNotFound();
            }

            var settings = app.GetSettings<PointOfSaleSettings>();

            // This is not obvious, but we must have a non-null currency or else request validation may not work correctly
            request.Currency ??= settings.Currency;

            ValidatePOSAppRequest(request);
            if (!string.IsNullOrEmpty(request.AppName))
            {
                ValidateAppRequest(request);
            }
            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            if (!string.IsNullOrEmpty(request.AppName))
            {
                app.Name = request.AppName;
            }
            if (request.Archived != null)
            {
                app.Archived = request.Archived.Value;
            }
            app.SetSettings(ToPointOfSaleSettings(request, settings));

            await _appService.UpdateOrCreateApp(app);

            return Ok(ToPointOfSaleModel(app));
        }

        [HttpGet("~/api/v1/apps")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetAllApps()
        {
            var apps = await _appService.GetAllApps(_userManager.GetUserId(User), includeArchived: true);

            return Ok(apps.Select(ToModel).ToArray());
        }

        [HttpGet("~/api/v1/stores/{storeId}/apps")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetAllApps(string storeId)
        {
            var apps = await _appService.GetAllApps(_userManager.GetUserId(User), false, storeId, true);

            return Ok(apps.Select(ToModel).ToArray());
        }

        [HttpGet("~/api/v1/apps/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetApp(string appId)
        {
            var app = await _appService.GetApp(appId, null, includeArchived: true);
            if (app == null)
            {
                return AppNotFound();
            }

            return Ok(ToModel(app));
        }

        [HttpGet("~/api/v1/apps/pos/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetPosApp(string appId)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType, includeArchived: true);
            if (app == null)
            {
                return AppNotFound();
            }

            return Ok(ToPointOfSaleModel(app));
        }

        [HttpGet("~/api/v1/apps/crowdfund/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetCrowdfundApp(string appId)
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType, includeArchived: true);
            if (app == null)
            {
                return AppNotFound();
            }

            return Ok(ToCrowdfundModel(app));
        }

        [HttpDelete("~/api/v1/apps/{appId}")]
        public async Task<IActionResult> DeleteApp(string appId)
        {
            var app = await _appService.GetApp(appId, null, includeArchived: true);
            if (app == null)
            {
                return AppNotFound();
            }

            await _appService.DeleteApp(app);

            return Ok();
        }

        private IActionResult AppNotFound()
        {
            return this.CreateAPIError(404, "app-not-found", "The app with specified ID was not found");
        }

        private CrowdfundSettings ToCrowdfundSettings(CrowdfundAppRequest request, CrowdfundSettings settings)
        {
            Enum.TryParse<BTCPayServer.Services.Apps.CrowdfundResetEvery>(request.ResetEvery.ToString(), true, out var resetEvery);
            
            if (request.Title is not null) settings.Title = request.Title.Trim();
            if (request.TargetCurrency is not null) settings.TargetCurrency = request.TargetCurrency.Trim();
            if (request.Description is not null) settings.Description = request.Description.Trim();
            if (request.MainImageUrl is not null) settings.MainImageUrl = request.MainImageUrl.Trim();
            if (request.NotificationUrl is not null) settings.NotificationUrl = request.NotificationUrl.Trim();
            if (request.Tagline is not null) settings.Tagline = request.Tagline.Trim();
            if (request.ResetEvery.HasValue) settings.ResetEvery = resetEvery;
            if (request.ResetEveryAmount.HasValue) settings.ResetEveryAmount = request.ResetEveryAmount ?? 1;
            if (request.Enabled.HasValue) settings.Enabled = request.Enabled.Value;
            if (request.EnforceTargetAmount.HasValue) settings.EnforceTargetAmount = request.EnforceTargetAmount.Value;
            if (request.StartDate.HasValue) settings.StartDate = request.StartDate?.UtcDateTime;
            if (request.EndDate.HasValue) settings.EndDate = request.EndDate?.UtcDateTime;
            if (request.TargetAmount.HasValue) settings.TargetAmount = request.TargetAmount.Value;
            if (request.AnimationsEnabled.HasValue) settings.AnimationsEnabled = request.AnimationsEnabled.Value;
            if (request.DisplayPerksValue.HasValue) settings.DisplayPerksValue = request.DisplayPerksValue.Value;
            if (request.DisplayPerksRanking.HasValue) settings.DisplayPerksRanking = request.DisplayPerksRanking.Value;
            if (request.SortPerksByPopularity.HasValue) settings.SortPerksByPopularity = request.SortPerksByPopularity.Value;
            // If Disqus shortname is not null or empty we assume that Disqus should be enabled
            if (request.DisqusShortname is not null) settings.DisqusShortname = request.DisqusShortname.Trim();
            settings.DisqusEnabled = request.DisqusEnabled is true && !string.IsNullOrEmpty(settings.DisqusShortname);
            // If explicit parameter is not passed for enabling sounds/animations, turn them on if custom sounds/colors are passed
            if (request.Sounds is not null) settings.Sounds = ValidateStringArray(request.Sounds);
            if (request.AnimationColors is not null) settings.AnimationColors = ValidateStringArray(request.AnimationColors);
            if (request.SoundsEnabled is not null) settings.SoundsEnabled = request.SoundsEnabled is true && settings.Sounds?.Any() is true;
            if (request.AnimationsEnabled is not null) settings.AnimationsEnabled = request.AnimationsEnabled is true && settings.AnimationColors?.Any() is true;
            if (request.PerksTemplate is not null) settings.PerksTemplate = AppService.SerializeTemplate(AppService.Parse(request.PerksTemplate.Trim()));
            if (request.FormId is not null) settings.FormId = request.FormId;
            
            return settings;
        }

        private PointOfSaleSettings ToPointOfSaleSettings(PointOfSaleAppRequest request, PointOfSaleSettings settings)
        {
            Enum.TryParse<BTCPayServer.Plugins.PointOfSale.PosViewType>(request.DefaultView.ToString(), true, out var defaultView);

            if (request.Title is not null) settings.Title = request.Title;
            if (request.Currency is not null) settings.Currency = request.Currency;
            if (request.DefaultView.HasValue) settings.DefaultView = defaultView;
            if (request.ShowItems.HasValue) settings.ShowItems = request.ShowItems.Value;
            if (request.ShowCustomAmount.HasValue) settings.ShowCustomAmount = request.ShowCustomAmount.Value;
            if (request.ShowDiscount.HasValue) settings.ShowDiscount = request.ShowDiscount.Value;
            if (request.ShowSearch.HasValue) settings.ShowSearch = request.ShowSearch.Value;
            if (request.ShowCategories.HasValue) settings.ShowCategories = request.ShowCategories.Value;
            if (request.EnableTips.HasValue) settings.EnableTips = request.EnableTips.Value;
            if (request.RedirectAutomatically.HasValue) settings.RedirectAutomatically = request.RedirectAutomatically.Value;
            if (request.Template is not null) settings.Template = AppService.SerializeTemplate(AppService.Parse(request.Template));
            if (request.RedirectUrl is not null) settings.RedirectUrl = request.RedirectUrl;
            if (request.NotificationUrl is not null) settings.NotificationUrl = request.NotificationUrl;
            if (request.Description is not null) settings.Description = request.Description;
            if (request.FixedAmountPayButtonText is not null) settings.ButtonText = request.FixedAmountPayButtonText;
            if (request.CustomAmountPayButtonText is not null) settings.CustomButtonText = request.CustomAmountPayButtonText;
            if (request.TipText is not null) settings.CustomTipText = request.TipText;
            if (request.CustomTipPercentages is not null) settings.CustomTipPercentages = request.CustomTipPercentages;
            if (request.FormId is not null) settings.FormId = request.FormId;
            
            return settings;
        }

        private AppBaseData ToModel(AppData appData)
        {
            return new AppBaseData
            {
                Id = appData.Id,
                Archived = appData.Archived,
                AppType = appData.AppType,
                AppName = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created,
            };
        }

        private AppBaseData ToModel(Models.AppViewModels.ListAppsViewModel.ListAppViewModel appData)
        {
            return new AppBaseData
            {
                Id = appData.Id,
                Archived = appData.Archived,
                AppType = appData.AppType,
                AppName = appData.AppName,
                StoreId = appData.StoreId,
                Created = appData.Created,
            };
        }

        private PointOfSaleAppData ToPointOfSaleModel(AppData appData)
        {
            var settings = appData.GetSettings<PointOfSaleSettings>();
            Enum.TryParse<PosViewType>(settings.DefaultView.ToString(), true, out var defaultView);
            
            return new PointOfSaleAppData
            {
                Id = appData.Id,
                Archived = appData.Archived,
                AppType = appData.AppType,
                AppName = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created,
                Title = settings.Title,
                DefaultView = defaultView,
                ShowItems = settings.ShowItems,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                ShowSearch = settings.ShowSearch,
                ShowCategories = settings.ShowCategories,
                EnableTips = settings.EnableTips,
                Currency = settings.Currency,
                FixedAmountPayButtonText = settings.ButtonText,
                CustomAmountPayButtonText = settings.CustomButtonText,
                TipText = settings.CustomTipText,
                CustomTipPercentages = settings.CustomTipPercentages,
                FormId = settings.FormId,
                NotificationUrl = settings.NotificationUrl,
                RedirectUrl = settings.RedirectUrl,
                Description = settings.Description,
                RedirectAutomatically = settings.RedirectAutomatically,
                Items = JsonConvert.DeserializeObject(
                    JsonConvert.SerializeObject(
                        AppService.Parse(settings.Template),
                        new JsonSerializerSettings
                        {
                            ContractResolver =
                                new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                        }
                    )
                )
            };
        }

        private void ValidatePOSAppRequest(PointOfSaleAppRequest request)
        {
            if (request.Currency != null && _currencies.GetCurrencyData(request.Currency, false) == null)
            {
                ModelState.AddModelError(nameof(request.Currency), "Invalid currency");
            }

            if (request.Template != null)
            {
                try
                {
                    // Just checking if we can serialize
                    AppService.SerializeTemplate(AppService.Parse(request.Template));
                }
                catch
                {
                    ModelState.AddModelError(nameof(request.Template), "Invalid template");
                }
            }
        }

        private CrowdfundAppData ToCrowdfundModel(AppData appData)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            Enum.TryParse<CrowdfundResetEvery>(settings.ResetEvery.ToString(), true, out var resetEvery);

            return new CrowdfundAppData
            {
                Id = appData.Id,
                Archived = appData.Archived,
                AppType = appData.AppType,
                AppName = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created,
                Title = settings.Title,
                Enabled = settings.Enabled,
                EnforceTargetAmount = settings.EnforceTargetAmount,
                StartDate = settings.StartDate,
                TargetCurrency = settings.TargetCurrency,
                Description = settings.Description,
                EndDate = settings.EndDate,
                TargetAmount = settings.TargetAmount,
                MainImageUrl = settings.MainImageUrl,
                NotificationUrl = settings.NotificationUrl,
                Tagline = settings.Tagline,
                DisqusEnabled = settings.DisqusEnabled,
                DisqusShortname = settings.DisqusShortname,
                SoundsEnabled = settings.SoundsEnabled,
                AnimationsEnabled = settings.AnimationsEnabled,
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = resetEvery,
                DisplayPerksValue = settings.DisplayPerksValue,
                DisplayPerksRanking = settings.DisplayPerksRanking,
                SortPerksByPopularity = settings.SortPerksByPopularity,
                Sounds = settings.Sounds,
                AnimationColors = settings.AnimationColors,
                Perks = JsonConvert.DeserializeObject(
                    JsonConvert.SerializeObject(
                        AppService.Parse(settings.PerksTemplate), 
                        new JsonSerializerSettings
                        {
                            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                        }
                    )
                )
            };
        }

        private string[]? ValidateStringArray(string[]? arr)
        {
            if (arr == null || !arr.Any())
            {
                return null;
            }

            // Make sure it's not just an array of empty strings
            if (arr.All(s => string.IsNullOrEmpty(s.Trim())))
            {
                return null;
            }

            return arr.Select(s => s.Trim()).ToArray();
        }

        private void ValidateCrowdfundAppRequest(CrowdfundAppRequest request)
        {
            if (request.TargetCurrency != null && _currencies.GetCurrencyData(request.TargetCurrency, false) == null)
            {
                ModelState.AddModelError(nameof(request.TargetCurrency), "Invalid currency");
            }

            if (request.PerksTemplate != null)
            {
                try
                {
                    // Just checking if we can serialize
                    AppService.SerializeTemplate(AppService.Parse(request.PerksTemplate));
                }
                catch
                {
                    ModelState.AddModelError(nameof(request.PerksTemplate), "Invalid template");
                }
            }

            if (request.ResetEvery.HasValue && request.ResetEvery != CrowdfundResetEvery.Never)
            {
                if (request.StartDate == null)
                {
                    ModelState.AddModelError(nameof(request.StartDate), "A start date is needed when the goal resets every X amount of time");
                }
                if (request.ResetEveryAmount <= 0)
                {
                    ModelState.AddModelError(nameof(request.ResetEveryAmount), "You must reset the goal at a minimum of 1");
                }
            }
            
            if (request.Sounds != null && ValidateStringArray(request.Sounds) == null)
            {
                ModelState.AddModelError(nameof(request.Sounds), "Sounds must be a non-empty array of non-empty strings");
            }

            if (request.AnimationColors != null && ValidateStringArray(request.AnimationColors) == null)
            {
                ModelState.AddModelError(nameof(request.AnimationColors), "Animation colors must be a non-empty array of non-empty strings");
            }

            if (request is { StartDate: not null, EndDate: not null } && DateTimeOffset.Compare((DateTimeOffset)request.StartDate, (DateTimeOffset)request.EndDate!) > 0)
            {
                ModelState.AddModelError(nameof(request.EndDate), "End date cannot be before start date");
            }
        }

        private void ValidateAppRequest(IAppRequest? request)
        {
            if (string.IsNullOrEmpty(request?.AppName))
            {
                ModelState.AddModelError(nameof(request.AppName), "App name is missing");
            }
            else if (request.AppName.Length is < 1 or > 50)
            {
                ModelState.AddModelError(nameof(request.AppName), "App name can only be between 1 and 50 characters");
            }
        }
    }
}
