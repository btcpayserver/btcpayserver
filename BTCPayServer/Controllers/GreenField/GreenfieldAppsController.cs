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
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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

        [HttpGet("~/api/v1/apps/{appId}/sales")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetAppSales(string appId, [FromQuery] int numberOfDays = 7)
        {
            var app = await _appService.GetApp(appId, null, includeArchived: true);
            if (app == null) return AppNotFound();

            var stats = await _appService.GetSalesStats(app, numberOfDays);
            return Ok(stats);
        }

        [HttpGet("~/api/v1/apps/{appId}/top-items")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetAppTopItems(string appId, [FromQuery] int offset = 0, [FromQuery] int count = 10)
        {
            var app = await _appService.GetApp(appId, null, includeArchived: true);
            if (app == null) return AppNotFound();

            var stats = (await _appService.GetItemStats(app)).ToList();
            var max = Math.Min(count, stats.Count - offset); 
            var items = stats.GetRange(offset, max);
            return Ok(items);
        }
        
        private IActionResult AppNotFound()
        {
            return this.CreateAPIError(404, "app-not-found", "The app with specified ID was not found");
        }

        private CrowdfundSettings ToCrowdfundSettings(CrowdfundAppRequest request, CrowdfundSettings settings)
        {
            var parsedSounds = ValidateStringArray(request.Sounds);
            var parsedColors = ValidateStringArray(request.AnimationColors);
            Enum.TryParse<BTCPayServer.Services.Apps.CrowdfundResetEvery>(request.ResetEvery.ToString(), true, out var resetEvery);
            
            return new CrowdfundSettings
            {
                Title = request.Title?.Trim() ?? request.AppName,
                Enabled = request.Enabled ?? true,
                EnforceTargetAmount = request.EnforceTargetAmount ?? false,
                StartDate = request.StartDate?.UtcDateTime,
                TargetCurrency = request.TargetCurrency?.Trim(),
                Description = request.Description?.Trim(),
                EndDate = request.EndDate?.UtcDateTime,
                TargetAmount = request.TargetAmount,
                MainImageUrl = request.MainImageUrl?.Trim(),
                NotificationUrl = request.NotificationUrl?.Trim(),
                Tagline = request.Tagline?.Trim(),
                PerksTemplate = request.PerksTemplate is not null ? AppService.SerializeTemplate(AppService.Parse(request.PerksTemplate.Trim())) : null,
                // If Disqus shortname is not null or empty we assume that Disqus should be enabled
                DisqusEnabled = !string.IsNullOrEmpty(request.DisqusShortname?.Trim()),
                DisqusShortname = request.DisqusShortname?.Trim(),
                // If explicit parameter is not passed for enabling sounds/animations, turn them on if custom sounds/colors are passed
                SoundsEnabled = request.SoundsEnabled ?? parsedSounds != null,
                AnimationsEnabled = request.AnimationsEnabled ?? parsedColors != null,
                ResetEveryAmount = request.ResetEveryAmount ?? 1,
                ResetEvery = resetEvery,
                DisplayPerksValue = request.DisplayPerksValue ?? false,
                DisplayPerksRanking = request.DisplayPerksRanking ?? false,
                SortPerksByPopularity = request.SortPerksByPopularity ?? false,
                Sounds = parsedSounds ?? new CrowdfundSettings().Sounds,
                AnimationColors = parsedColors ?? new CrowdfundSettings().AnimationColors,
                FormId = request.FormId
            };
        }

        private PointOfSaleSettings ToPointOfSaleSettings(PointOfSaleAppRequest request, PointOfSaleSettings settings)
        {
            Enum.TryParse<BTCPayServer.Plugins.PointOfSale.PosViewType>(request.DefaultView.ToString(), true, out var defaultView);

            return new PointOfSaleSettings
            {
                Title = request.Title ?? request.AppName,
                DefaultView = defaultView,
                ShowItems = request.ShowItems ?? false,
                ShowCustomAmount = request.ShowCustomAmount ?? false,
                ShowDiscount = request.ShowDiscount ?? false,
                ShowSearch = request.ShowSearch ?? false,
                ShowCategories = request.ShowCategories ?? false,
                EnableTips = request.EnableTips ?? false,
                Currency = request.Currency,
                Template = request.Template != null ? AppService.SerializeTemplate(AppService.Parse(request.Template)) : null,
                ButtonText = request.FixedAmountPayButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = request.CustomAmountPayButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = request.TipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomTipPercentages = request.CustomTipPercentages,
                NotificationUrl = request.NotificationUrl,
                RedirectUrl = request.RedirectUrl,
                Description = request.Description,
                RedirectAutomatically = request.RedirectAutomatically,
                FormId = request.FormId
            };
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
                    AppService.SerializeTemplate(AppService.Parse(request.Template, true, true));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(nameof(request.Template), ex.Message);
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
                    AppService.SerializeTemplate(AppService.Parse(request.PerksTemplate, true, true));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(nameof(request.PerksTemplate), $"Invalid template: {ex.Message}");
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
